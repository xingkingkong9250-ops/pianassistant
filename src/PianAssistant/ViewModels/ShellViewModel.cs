using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PianAssistant.Helpers;
using PianAssistant.Models;
using PianAssistant.Services;

namespace PianAssistant.ViewModels;

/// <summary>
/// Root ViewModel for the main window (W1). Holds all sub-VMs and
/// the <see cref="ServiceRegistry"/>. Handles file open, sidebar
/// panel switching, settings, and event wiring between sub-VMs.
/// </summary>
public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly ServiceRegistry _services;
    private readonly bool _ownsServices;
    private readonly HotkeyCommandMap _hotkeyMap;
    private SidebarPanel _activePanel = SidebarPanel.Notes;
    private bool _isFullscreen;
    // 左侧播放列表默认收起；右侧侧边栏默认展开（用户要求笔记列表默认展开）
    private bool _isPlaylistVisible;
    private bool _isSidebarVisible = true;
    private bool _disposed;
    // 会话还原过程中抑制变更上报，避免把还原动作本身再写回数据库（M7）。
    private bool _isRestoring;
    // 片段入点(毫秒) → 导出输出路径：项目保存时回查用（统一时间线后不再经活动流记录）。
    private readonly Dictionary<double, string> _clipOutputPaths = new();
    // 音量防抖落库：拖动滑块高频变化时只在停止 500ms 后写 config.json。
    private CancellationTokenSource? _volumeSaveCts;
    // 播放位置按秒节流落库，避免高频写库（M7）。
    private long _lastMarkedSecond = -1;

    public ServiceRegistry Services => _services;
    public PlayerViewModel Player { get; }
    public PlaylistViewModel Playlist { get; }
    public NotesViewModel Notes { get; }
    public ClipListViewModel Clips { get; }
    public ScreenshotGalleryViewModel Screenshots { get; }
    public SettingsViewModel Settings { get; }
    /// <summary>批量文本操作预览窗 VM（M5）。</summary>
    public BatchPreviewViewModel Batch { get; }

    /// <summary>
    /// 请求确认「清空全部标记」（裁切点 + 截图标记）：
    /// 携带（裁切点数, 截图数），由 View 层弹确认框，确认后调用 <see cref="ExecuteClearAllMarks"/>。
    /// </summary>
    public event EventHandler<(int CutCount, int ShotCount)>? ConfirmClearAllMarksRequested;

    private void RequestClearAllMarks()
    {
        var cuts = Player.CutPoints.Count;
        var shots = Notes.Screenshots.Count;
        if (cuts == 0 && shots == 0) return; // 无标记可清，不打扰
        ConfirmClearAllMarksRequested?.Invoke(this, (cuts, shots));
    }

    /// <summary>
    /// 执行清空全部标记：删除全部截图记录（画廊与笔记同步，png 文件保留、
    /// 时间轴截图标记随之消失）并清空全部裁切点（分段重算为空）。
    /// </summary>
    public void ExecuteClearAllMarks()
    {
        if (_isRestoring) return;
        foreach (var entry in Notes.Screenshots.ToList())
            Screenshots.Remove(entry);
        Notes.ClearScreenshots();
        Player.ClearCutPoints();
    }

    /// <summary>
    /// 按起止时间切换片段的 ☑ 勾选标记（X 键桥接）：
    /// 时间轴黄带 ⇋ 拉片笔记片段条目勾选双向同一数据源（IsMarked）。
    /// </summary>
    private void ToggleSegmentMark(TimeSpan start, TimeSpan end)
    {
        var item = Notes.Items.OfType<SegmentListItem>()
            .FirstOrDefault(i => i.Segment.Start == start && i.Segment.End == end);
        if (item is null) return;
        item.IsMarked = !item.IsMarked;
    }

    /// <summary>Currently visible sidebar panel (Playlist / Notes / Clips).</summary>
    public SidebarPanel ActivePanel
    {
        get => _activePanel;
        set
        {
            if (SetProperty(ref _activePanel, value))
                OnPropertyChanged(nameof(ActivePanelTitle));
        }
    }

    /// <summary>侧边栏面板标题（面板头显示用）。</summary>
    public string ActivePanelTitle => _activePanel switch
    {
        SidebarPanel.Notes => "拉片笔记",
        SidebarPanel.Clips => "📦 片段导出",
        _ => string.Empty,
    };

    public bool IsFullscreen
    {
        get => _isFullscreen;
        set => SetProperty(ref _isFullscreen, value);
    }

    /// <summary>左侧播放列表面板是否展开（默认收起）。</summary>
    public bool IsPlaylistVisible
    {
        get => _isPlaylistVisible;
        set => SetProperty(ref _isPlaylistVisible, value);
    }

    /// <summary>右侧侧边栏（笔记/片段导出）是否展开（默认收起）。</summary>
    public bool IsSidebarVisible
    {
        get => _isSidebarVisible;
        set => SetProperty(ref _isSidebarVisible, value);
    }

    // --- Commands ---

    public ICommand OpenFileCommand { get; }
    public ICommand ToggleSidebarPanelCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand ShowNotesCommand { get; }
    /// <summary>切换到片段批量导出面板。</summary>
    public ICommand ShowClipsCommand { get; }
    /// <summary>打开截图画廊窗口（View 层订阅 GalleryRequested 创建窗口）。</summary>
    public ICommand ShowGalleryCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    /// <summary>跳到上一个活动标记（[ 键）。</summary>
    public ICommand PreviousNoteCommand { get; }
    /// <summary>跳到下一个活动标记（] 键）。</summary>
    public ICommand NextNoteCommand { get; }
    /// <summary>打开批量文本操作窗口（View 层订阅事件创建窗口）。</summary>
    public ICommand ShowBatchCommand { get; }

    // --- 项目文件命令（M6 全功能整合；IO/对话框在 View 层）---

    /// <summary>保存当前项目到数据库（View 层订阅事件后调用 SaveCurrentProject）。</summary>
    public ICommand SaveProjectCommand { get; }
    /// <summary>打开项目包（*.paproj）并加载到会话。</summary>
    public ICommand OpenProjectCommand { get; }
    /// <summary>把项目打包导出为 *.paproj。</summary>
    public ICommand ExportProjectCommand { get; }
    /// <summary>导入项目包（*.paproj）并加载到会话。</summary>
    public ICommand ImportProjectCommand { get; }
    /// <summary>展开/收起左侧播放列表面板。</summary>
    public ICommand TogglePlaylistVisibilityCommand { get; }
    /// <summary>展开/收起右侧侧边栏面板。</summary>
    public ICommand ToggleSidebarVisibilityCommand { get; }

    // --- Events ---

    public event EventHandler<string>? OpenFileRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? SettingsRequested;
    /// <summary>打开截图画廊窗口（View 层负责创建/聚焦窗口）。</summary>
    public event EventHandler? GalleryRequested;
    /// <summary>打开批量文本操作窗口（App 层负责创建窗口）。</summary>
    public event EventHandler? ShowBatchRequested;

    /// <summary>保存项目到数据库请求（View 层弹提示后调用 SaveCurrentProject）。</summary>
    public event EventHandler? SaveProjectRequested;
    /// <summary>打开项目包请求（View 层弹 OpenFileDialog 后调用 ImportAndLoadProject）。</summary>
    public event EventHandler? OpenProjectRequested;
    /// <summary>打包导出项目请求（View 层弹 SaveFileDialog 后调用 ExportCurrentProject）。</summary>
    public event EventHandler? ExportProjectRequested;
    /// <summary>导入项目包请求（View 层弹 OpenFileDialog 后调用 ImportAndLoadProject）。</summary>
    public event EventHandler? ImportProjectRequested;

    /// <summary>需要以弹窗告知用户的提示信息（如视频文件缺失）；由 App/View 层订阅弹出。</summary>
    public event EventHandler<string>? NotificationRequested;

    public ShellViewModel(ServiceRegistry services, bool ownsServices = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _ownsServices = ownsServices;

        Player = new PlayerViewModel(
            services.Playback, services.Screenshots, services.Clips,
            () => services.Settings.SeekStepFrames,
            () => services.Settings.RateStep,
            () => services.Settings.MaxRate,
            initialVolume: services.Settings.Volume,
            volumePersisted: OnVolumePersisted);
        // 清空全部标记（Ctrl+Shift+C / W1-R1-11）：先由 View 层确认再执行
        Player.ClearAllMarksRequested += (_, _) => RequestClearAllMarks();
        // 标记/取消标记当前选中片段（X / W1-R3）：桥接到笔记 ☑ 勾选标记（时间轴黄带 ⇋ 拉片笔记）
        Player.ToggleSegmentMarkRequested += (_, seg) => ToggleSegmentMark(seg.Start, seg.End);
        Playlist = new PlaylistViewModel();
        // Notes needs the current playback position to timestamp new notes.
        Notes = new NotesViewModel(() => Player.Position);
        // 片段批量导出：抽取走 ffmpeg 服务；拼接走 ConcatService（均经 Task.Run 保持异步）。
        Clips = new ClipListViewModel(
            (range, reencode, ct, onProgress) =>
                System.Threading.Tasks.Task.Run(
                    () => services.Clips.Extract(range, reencode, ct, onProgress)),
            (ranges, outputPath, ct, onProgress) =>
                services.Concats.ConcatAsync(ranges, outputPath, ct, onProgress));
        Screenshots = new ScreenshotGalleryViewModel();
        Settings = new SettingsViewModel(services.Settings, services.Hotkeys);
        // 设置保存后刷新播放器的最高倍率上限（下拉框档位 + 夹紧当前倍速）。
        Settings.Saved += (_, _) => Player.SetMaxRate(services.Settings.MaxRate);

        // 片段缩略图：ffmpeg 抽段起始帧，缓存到数据根 clips/thumbs/。
        Notes.Thumbnails = SegmentThumbnailService.Create(services.Ffmpeg, services.Settings.ClipsDirectory);

        // Wire Player → panel VMs（统一时间线：截图进笔记列表；片段只记录输出路径供项目保存回查）.
        Player.ScreenshotCaptured += (_, entry) =>
        {
            Screenshots.Add(entry);
            Notes.AddScreenshot(entry);
        };
        Player.ClipExtracted += (_, e) =>
        {
            Clips.Add(e.Range);
            _clipOutputPaths[e.Range.InPoint.TotalMilliseconds] = e.OutputPath;
        };
        Player.SegmentExported += (_, e) =>
        {
            Clips.Add(e.Range);
            _clipOutputPaths[e.Range.InPoint.TotalMilliseconds] = e.OutputPath;
        };

        // 裁切点变化 → 重算分段并重建统一时间线列表
        Player.CutPoints.CollectionChanged += OnCutPointsChanged;
        // 会话还原时 VLC 尚未解析出时长（Length=0），分段被跳过；
        // 时长就绪后（0 → 实际值）若已有裁切点则补算一次分段。
        Player.PropertyChanged += OnPlayerLengthChanged;

        // Wire Playlist → Player.
        Playlist.CurrentChanged += OnPlaylistCurrentChanged;

        // Wire Notes → Player (seek to note position).
        Notes.SeekRequested += (_, ts) => Player.SeekPercent(
            Player.Length > TimeSpan.Zero
                ? ts.TotalMilliseconds / Player.Length.TotalMilliseconds
                : 0);

        // Wire Player fullscreen → Shell.
        Player.FullscreenToggleRequested += (_, _) => IsFullscreen = !IsFullscreen;
        Player.FullscreenExitRequested += (_, _) => IsFullscreen = false;
        // 非循环模式播放结束 → 自动推进播放列表
        Player.EndReached += OnPlayerEnded;

        // 批量文本操作（M5）：裁切点经回调分发到播放器；标记指令已不支持（文字笔记随统一时间线移除）。
        Batch = new BatchPreviewViewModel(
            new BatchTextParser(),
            services.Batches,
            position => Player.AddCutPointAt(position),
            () => Player.CurrentPath);

        // 项目服务会话钩子（M6）：保存时把会话数据桥接到持久化层；加载时反向应用到会话。
        WireProjectService(services);

        // 素材会话自动保存/恢复钩子（M7）。
        WireMediaSession(services);

        // Commands.
        OpenFileCommand = new RelayCommand(RequestOpenFile);
        ToggleSidebarPanelCommand = new RelayCommand(
            () => ActivePanel = ActivePanel == SidebarPanel.Notes
                ? SidebarPanel.Clips
                : SidebarPanel.Notes);
        ShowNotesCommand = new RelayCommand(
            () => ActivePanel = SidebarPanel.Notes);
        ShowClipsCommand = new RelayCommand(
            () => ActivePanel = SidebarPanel.Clips);
        TogglePlaylistVisibilityCommand = new RelayCommand(
            () => IsPlaylistVisible = !IsPlaylistVisible);
        ToggleSidebarVisibilityCommand = new RelayCommand(
            () => IsSidebarVisible = !IsSidebarVisible);
        ShowGalleryCommand = new RelayCommand(
            () => GalleryRequested?.Invoke(this, EventArgs.Empty));
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty));
        OpenSettingsCommand = new RelayCommand(
            () => SettingsRequested?.Invoke(this, EventArgs.Empty));
        // 上/下一个活动标记跳转（转发到 Notes）
        PreviousNoteCommand = new RelayCommand(Notes.JumpToPrevious);
        NextNoteCommand = new RelayCommand(Notes.JumpToNext);
        // 批量文本操作窗口入口
        ShowBatchCommand = new RelayCommand(() => ShowBatchRequested?.Invoke(this, EventArgs.Empty));
        // 项目文件入口（View 层订阅后弹对话框）
        SaveProjectCommand = new RelayCommand(() => SaveProjectRequested?.Invoke(this, EventArgs.Empty));
        OpenProjectCommand = new RelayCommand(() => OpenProjectRequested?.Invoke(this, EventArgs.Empty));
        ExportProjectCommand = new RelayCommand(() => ExportProjectRequested?.Invoke(this, EventArgs.Empty));
        ImportProjectCommand = new RelayCommand(() => ImportProjectRequested?.Invoke(this, EventArgs.Empty));

        // 动作名 → 命令 映射（供 MainWindow 动态重建 InputBindings）。
        _hotkeyMap = new HotkeyCommandMap(Player, ToggleSidebarPanelCommand, PreviousNoteCommand, NextNoteCommand);
    }

    /// <summary>
    /// Open a media file path: registers it in the playlist, makes it
    /// current, tells the player to load it, and adds it to recent files.
    /// </summary>
    public void OpenMedia(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var media = Playlist.AddPath(path);
        Playlist.Select(media);
        _services.Settings.AddRecentFile(path);
        // Auto-play after opening.
        Player.PlayPauseCommand.Execute(null);
    }

    /// <summary>
    /// 批量打开媒体文件：依次注册进播放列表，最后选中并播放第一个新加入的条目。
    /// </summary>
    public void OpenMedia(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var list = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
        if (list.Count == 0) return;
        if (list.Count == 1)
        {
            OpenMedia(list[0]);
            return;
        }
        // 多选：全部加入播放列表，最后选中第一个并播放
        MediaFile? firstAdded = null;
        foreach (var path in list)
        {
            var media = Playlist.AddPath(path);
            firstAdded ??= media;
            _services.Settings.AddRecentFile(path);
        }
        if (firstAdded is not null)
        {
            Playlist.Select(firstAdded);
            Player.PlayPauseCommand.Execute(null);
        }
    }

    private void RequestOpenFile()
    {
        // The view (code-behind) handles the actual dialog and calls OpenMedia.
        // Use a sentinel string to trigger the file dialog; the view intercepts.
        OpenFileRequested?.Invoke(this, string.Empty);
    }

    private void OnPlaylistCurrentChanged(object? sender, MediaFile? media)
    {
        if (media is null) return;
        // 文件被移动/删除：友好提示，不崩溃（列表项已标灰）
        if (!File.Exists(media.Path))
        {
            NotificationRequested?.Invoke(this, $"视频文件不存在或已被移动：\n{media.Path}");
            return;
        }
        try
        {
            // 切换前先把上一个素材的会话落库（M7）。
            _services.Sessions.EndSession();
            // 重载过程中抑制变更上报，避免把清空动作本身写回数据库（M7）。
            _isRestoring = true;
            try
            {
                Player.Open(media.Path);
                _services.Settings.AddRecentFile(media.Path);
                // 打开新视频时清空截图和段
                Notes.ClearAll();
            }
            finally
            {
                _isRestoring = false;
                _lastMarkedSecond = -1;
            }
            // 开始跟踪新素材：无论是否开启自动恢复，发生变更都应能保存（M7）。
            _services.Sessions.BeginSession(media);
            if (_services.Settings.RestoreSession)
            {
                // 静默恢复：先按快速键同步还原，后台算完内容哈希后再按需升级（M7）。
                _ = _services.Sessions.LoadAsync(media);
            }
        }
        catch (FileNotFoundException ex)
        {
            NotificationRequested?.Invoke(this,
                $"视频文件不存在或已被移动：\n{ex.FileName ?? media.Path}");
        }
        catch (Exception ex)
        {
            AppLog.Error("[ShellVM] 打开视频失败", ex);
            NotificationRequested?.Invoke(this, $"无法打开视频：{ex.Message}");
        }
    }

    /// <summary>
    /// 非循环模式播放结束时推进播放列表并自动播放下一曲。
    /// EndReached 来自 LibVLC 线程，需切回 UI 线程操作集合。
    /// </summary>
    private void OnPlayerEnded(object? sender, EventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            if (Playlist.MoveNext())
            {
                // MoveNext 已通过 CurrentChanged → OnPlaylistCurrentChanged → Player.Open
                // 自动加载下一曲；这里启动播放
                Player.PlayPauseCommand.Execute(null);
            }
        });
    }

    /// <summary>裁切点集合变化时重算分段并重建笔记面板统一时间线（删除裁切点即合并相邻片段）。</summary>
    private void OnCutPointsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Notes.RecomputeSegments(Player.CutPoints.ToList(), Player.Length, Player.CurrentPath);
    }

    /// <summary>
    /// 时长从 0 变为实际值时补算分段：打开旧会话时还原先于 VLC 解析出时长，
    /// RecomputeSegments 因 length=0 跳过建段，导致片段列表为空直到下一次交互。
    /// </summary>
    private void OnPlayerLengthChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlayerViewModel.Length)) return;
        if (Player.Length <= TimeSpan.Zero || Player.CutPoints.Count == 0) return;
        Notes.RecomputeSegments(Player.CutPoints.ToList(), Player.Length, Player.CurrentPath);
    }

    // --- 裁切点导入/导出（M5，路径由 View 层对话框确定）---

    /// <summary>导出裁切点清单：每行一个裁切点，并自动生成片段行（片段 #N）。</summary>
    public void ExportCutPoints(string path) =>
        ClipExportService.ExportCutPoints(path, Player.CutPoints.ToList(), Player.Length);

    /// <summary>
    /// 从文本文件导入裁切点（替换现有集合；集合变化自动同步时间轴与分段列表）；
    /// 文件中的片段行加入片段批量导出队列。
    /// </summary>
    public void ImportCutPoints(string path)
    {
        var (points, segments) = ClipExportService.ImportCutPoints(path, Player.CurrentPath);
        Player.SetCutPoints(points);
        foreach (var segment in segments)
            Clips.Add(segment);
    }

    /// <summary>
    /// 导入人工编辑过的「导出数据」CSV 解析结果，覆盖当前视频的笔记数据
    /// （W1-C2-D-02）：① 裁切点以文件为准覆盖（自动重算分段）；
    /// ② 文件截图清单未列出的现有截图记录移除（png 文件保留不删）；
    /// ③ 重建完成后以文件为标记真值源，逐条设置截图/片段勾选标记；
    /// ④ 文件缺失的截图行按时间码后台 ffmpeg 重新抽帧（v1.2.0）。
    /// </summary>
    public void ImportNotesData(NotesExportData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        // 1. 截图行分类：① 文件路径在磁盘存在 → 保留/回填（不抽帧）；
        //    ② 文件列空或 png 缺失 → 记时间码待后台重建；③ 时间码无效的行丢弃。
        var keptFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rebuilds = new List<(TimeSpan Position, bool Marked)>();
        foreach (var s in data.Screenshots)
        {
            if (s.File.Length > 0 && File.Exists(s.File))
            {
                keptFiles.Add(s.File);
                continue;
            }
            if (NotesDataCsvService.TryParseTime(s.Position, out var pos) && pos > TimeSpan.Zero)
                rebuilds.Add((pos, s.Marked));
        }
        bool managesScreenshots = data.Screenshots.Count > 0;
        if (managesScreenshots)
        {
            // 未被 CSV 引用的现有记录移除（png 文件保留不删）
            foreach (var shot in Screenshots.Items
                         .Where(s => !keptFiles.Contains(s.OutputPath)).ToList())
            {
                Notes.RemoveScreenshot(shot);
            }
            // CSV 有效文件行在集合中缺失时直接回填记录（换机/目录迁移但 png 已拷贝场景）
            var existing = Screenshots.Items.Select(s => s.OutputPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var s in data.Screenshots)
            {
                if (!keptFiles.Contains(s.File) || existing.Contains(s.File)) continue;
                var pos = NotesDataCsvService.TryParseTime(s.Position, out var p) ? p : TimeSpan.Zero;
                var entry = new ScreenshotEntry(
                    Player.CurrentPath ?? string.Empty, s.File, pos, DateTime.Now);
                Screenshots.Add(entry);
                Notes.AddScreenshot(entry);
            }
        }

        // 2. 裁切点覆盖（集合变化自动触发分段重算与列表重建）
        var points = new List<TimeSpan>();
        foreach (var text in data.CutPoints)
        {
            if (NotesDataCsvService.TryParseTime(text, out var t) && t > TimeSpan.Zero)
                points.Add(t);
        }
        Player.SetCutPoints(points.Distinct().OrderBy(t => t).ToList());

        // 3. 标记恢复：以 CSV 为真值源强制设置（含未标记的显式重置）；
        //    重建中的缺失截图标记在抽帧完成入列表时按 CSV 值设置。
        static TimeSpan T(string text) =>
            NotesDataCsvService.TryParseTime(text, out var t) ? t : TimeSpan.Zero;
        var segMarks = new HashSet<(long Start, long End)>(
            data.Segments.Where(s => s.Marked)
                .Select(s => (T(s.Start).Ticks, T(s.End).Ticks)));
        var shotMarks = new HashSet<string>(
            data.Screenshots.Where(s => s.Marked && s.File.Length > 0).Select(s => s.File),
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in Notes.Items)
        {
            switch (item)
            {
                case SegmentListItem seg:
                    seg.IsMarked = segMarks.Contains((seg.Segment.Start.Ticks, seg.Segment.End.Ticks));
                    break;
                case ScreenshotListItem shot:
                    shot.IsMarked = managesScreenshots && shotMarks.Contains(shot.Entry.OutputPath);
                    break;
            }
        }

        // 4. 缺失截图后台重建：按时间码 ffmpeg 抽帧，完成即入列表
        if (rebuilds.Count > 0)
            RebuildMissingScreenshots(rebuilds);
    }

    /// <summary>轻量提示请求（如导入重建截图汇总），View 层弹 toast。</summary>
    public event EventHandler<string>? ToastRequested;

    /// <summary>
    /// 缺失截图后台重建（v1.2.0）：按导入数据的时间码逐条 ffmpeg 从源视频
    /// 抽帧，产物校验通过后回 UI 线程入画廊与笔记列表并按 CSV 设置勾选；
    /// 全部结束后 toast 汇总。单条失败不中断其余重建。
    /// </summary>
    private void RebuildMissingScreenshots(IReadOnlyList<(TimeSpan Position, bool Marked)> shots)
    {
        if (string.IsNullOrEmpty(Player.CurrentPath)) return;
        var screenshotService = _services.Screenshots;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        _ = Task.Run(async () =>
        {
            int ok = 0, fail = 0;
            foreach (var (pos, marked) in shots)
            {
                try
                {
                    var entry = screenshotService.CaptureAt(pos);
                    if (!File.Exists(entry.OutputPath))
                        throw new InvalidOperationException("抽帧产物未生成（时间码可能超出视频时长）");
                    await dispatcher.InvokeAsync(() =>
                    {
                        Screenshots.Add(entry);
                        Notes.AddScreenshot(entry);
                        var item = Notes.Items.OfType<ScreenshotListItem>()
                            .FirstOrDefault(i => i.Entry.Position == entry.Position);
                        if (item is not null) item.IsMarked = marked;
                    });
                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    AppLog.Error($"[ShellVM] 导入重建截图失败 @{pos:hh\\:mm\\:ss\\.fff}", ex);
                }
            }
            await dispatcher.InvokeAsync(() => ToastRequested?.Invoke(this,
                $"导入完成：重建缺失截图 {ok}/{shots.Count}" +
                (fail > 0 ? $"（失败 {fail}）" : string.Empty)));
        });
    }

    /// <summary>
    /// 音量持久化（软件级记忆，不写入视频数据）：拖动滑块时高频触发，
    /// 防抖 500ms 后才写 config.json，只保留最后一次值。
    /// </summary>
    private void OnVolumePersisted(int volume)
    {
        _volumeSaveCts?.Cancel();
        _volumeSaveCts = new CancellationTokenSource();
        var token = _volumeSaveCts.Token;
        _ = Task.Delay(500, token).ContinueWith(
            t =>
            {
                if (t.IsCanceled) return;
                try { _services.Settings.Volume = volume; }
                catch (Exception ex) { AppLog.Error("[ShellVM] 音量保存失败", ex); }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    public void Dispose()
    {
        if (_disposed) return;
        // 退出前冲刷防抖中的音量（拖动后 500ms 内退出也能记住）
        try { _services.Settings.Volume = Player.Volume; }
        catch { /* 设置盘写失败不阻塞退出 */ }
        // 退出前把当前素材会话落库（M7）；须在释放播放器之前采集快照。
        try { _services.Sessions.EndSession(); }
        catch (Exception ex) { AppLog.Error("[ShellVM] 会话落库失败", ex); }
        _disposed = true;
        Player.Dispose();
        if (_ownsServices)
            (_services.Playback as IDisposable)?.Dispose();
    }

    // --- 项目文件（M6 全功能整合）---

    /// <summary>
    /// 把 ProjectService 的会话访问器接到当前 VM：保存时汇总视频路径/截图/片段；
    /// 加载时把读回的数据还原到播放列表、截图列表与片段队列。
    /// 文字笔记已随统一时间线重构移除：ResolveNotes/ApplyNotes 不再赋值
    /// （旧项目包中的笔记数据静默忽略，数据库 notes 表保留不动）。
    /// </summary>
    private void WireProjectService(ServiceRegistry services)
    {
        services.Projects.ResolveSourcePath = () => Player.CurrentPath;
        services.Projects.ResolveScreenshots = () => Screenshots.Items.ToList();
        services.Projects.ResolveClips = () =>
            Clips.Items
                .Select(clip => (clip, FindClipOutputPath(clip)))
                .ToList();

        services.Projects.ApplySourcePath = path =>
        {
            if (!string.IsNullOrWhiteSpace(path)) OpenMedia(path);
        };
        services.Projects.ApplyScreenshots = list =>
        {
            Screenshots.Clear();
            Notes.ClearAll();
            foreach (var screenshot in list)
            {
                Screenshots.Add(screenshot);
                Notes.AddScreenshot(screenshot);
            }
        };
        services.Projects.ApplyClips = list =>
        {
            Clips.Clear();
            foreach (var (range, outputPath) in list)
            {
                Clips.Add(range);
                if (!string.IsNullOrWhiteSpace(outputPath))
                    _clipOutputPaths[range.InPoint.TotalMilliseconds] = outputPath;
            }
        };
    }

    // --- 素材会话自动保存/恢复（M7）---

    /// <summary>
    /// 把会话服务的采集/应用委托接上当前 VM，并订阅各变更源触发防抖落库：
    /// 裁切点、截图、片段、笔记活动流、入出点、倍速与播放位置。
    /// </summary>
    private void WireMediaSession(ServiceRegistry services)
    {
        var sessions = services.Sessions;

        // 采集当前会话快照供落库（文字笔记已移除，Notes 传空集合）。
        sessions.CaptureSnapshot = () => new MediaSessionSnapshot
        {
            Path = Player.CurrentPath ?? string.Empty,
            Position = Player.Position,
            Rate = Player.Rate,
            InPoint = Player.InPoint,
            OutPoint = Player.OutPoint,
            CutPoints = Player.CutPoints.ToList(),
            Screenshots = Screenshots.Items.ToList(),
            Notes = Array.Empty<NoteEntry>(),
        };

        // 把读回的会话应用到当前会话（服务保证在 UI 线程回调）。
        sessions.ApplySession = ApplySession;

        // 成功还原后以提示条展示统计（不弹窗打断）。
        sessions.SessionRestored += (_, r) => NotificationRequested?.Invoke(this, r.Summary);

        // 各类变更 → 防抖落库。
        Player.CutPoints.CollectionChanged += (_, _) => MarkSessionDirty();
        Player.ScreenshotCaptured += (_, _) => MarkSessionDirty();
        Player.ClipExtracted += (_, _) => MarkSessionDirty();
        Player.SegmentExported += (_, _) => MarkSessionDirty();
        Notes.Changed += (_, _) => MarkSessionDirty();
        Player.PropertyChanged += OnPlayerPropertyChangedForSession;
    }

    /// <summary>把读回的会话数据还原到播放器、画廊与笔记面板（旧会话笔记静默忽略）。</summary>
    private void ApplySession(MediaSession session)
    {
        _isRestoring = true;
        try
        {
            // 先清空，再还原——否则清空会把刚由 SetCutPoints 重建的分段/裁切点一并抹掉。
            Screenshots.Clear();
            Notes.ClearAll();

            Player.SetCutPoints(session.CutPointsMs.Select(ms => TimeSpan.FromMilliseconds(ms)));
            Player.SetInOut(
                session.InPointMs is { } inMs ? TimeSpan.FromMilliseconds(inMs) : null,
                session.OutPointMs is { } outMs ? TimeSpan.FromMilliseconds(outMs) : null);

            foreach (var s in session.Screenshots)
            {
                if (string.IsNullOrWhiteSpace(s.FilePath)) continue;
                var entry = new ScreenshotEntry(
                    session.Path, s.FilePath, TimeSpan.FromMilliseconds(s.PositionMs),
                    s.CapturedAt, s.Label);
                Screenshots.Add(entry);
                Notes.AddScreenshot(entry);
            }

            // 还原播放位置与倍速（位置为 0/倍速 1.0 时即等同不还原）。
            Player.Position = TimeSpan.FromMilliseconds(session.LastPositionMs);
            Player.Rate = session.Rate;
            _lastMarkedSecond = (long)Player.Position.TotalSeconds;
        }
        finally
        {
            _isRestoring = false;
        }
    }

    /// <summary>响播放器属性变化：倍速/入出点即时落库，位置按秒节流。</summary>
    private void OnPlayerPropertyChangedForSession(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerViewModel.Rate):
            case nameof(PlayerViewModel.InPoint):
            case nameof(PlayerViewModel.OutPoint):
                MarkSessionDirty();
                break;
            case nameof(PlayerViewModel.Position):
                MarkSessionDirtyThrottled();
                break;
        }
    }

    /// <summary>播放位置按秒节流：同一秒内的多次位置变化只上报一次。</summary>
    private void MarkSessionDirtyThrottled()
    {
        var second = (long)Player.Position.TotalSeconds;
        if (second == _lastMarkedSecond) return;
        _lastMarkedSecond = second;
        MarkSessionDirty();
    }

    /// <summary>标记会话已变更（还原过程中与已释放时不上报）。</summary>
    private void MarkSessionDirty()
    {
        if (_isRestoring || _disposed) return;
        _services.Sessions.MarkDirty();
    }

    /// <summary>按片段入点回查其导出输出路径（本次会话内经 ClipExtracted/SegmentExported 记录）。</summary>
    private string? FindClipOutputPath(ClipRange clip) =>
        _clipOutputPaths.TryGetValue(clip.InPoint.TotalMilliseconds, out var path) ? path : null;

    /// <summary>
    /// 把指定片段导出到用户选定的目录（笔记面板「导出此段/导出选中」用）：
    /// 走 ffmpeg 流拷贝切出 mp4，文件名沿用自动命名规则。
    /// </summary>
    public string ExportSegmentTo(TimelineSegment segment, string targetDirectory)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        var source = segment.SourcePath ?? Player.CurrentPath
            ?? throw new InvalidOperationException("没有已打开的视频，无法导出片段。");
        var range = new ClipRange(source, segment.Start, segment.End);
        var outputPath = _services.Clips.ExtractTo(range, targetDirectory);
        _clipOutputPaths[range.InPoint.TotalMilliseconds] = outputPath;
        return outputPath;
    }

    /// <summary>按动作名解析命令（热键一致性测试用）。</summary>
    public ICommand? ResolveCommand(string actionName) => _hotkeyMap.ResolveCommand(actionName);

    /// <summary>解析动作对应的命令 + 参数（MainWindow 动态重建 InputBindings 用）。</summary>
    public HotkeyBinding? ResolveHotkey(HotkeyAction action) => _hotkeyMap.Resolve(action);

    /// <summary>把当前会话保存到数据库；返回结果（含新项目 ID）。</summary>
    public ProjectOperationResult SaveCurrentProject()
    {
        var id = _services.Projects.SaveProject(ResolveProjectName());
        return id is null
            ? ProjectOperationResult.Fail("保存项目失败（数据库不可用）。")
            : ProjectOperationResult.Ok(id);
    }

    /// <summary>把当前会话保存到数据库并打包导出为 *.paproj 到指定路径。</summary>
    public ProjectOperationResult ExportCurrentProject(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath))
            return ProjectOperationResult.Fail("未指定导出路径。");
        var id = _services.Projects.SaveProject(ResolveProjectName());
        if (id is null)
            return ProjectOperationResult.Fail("保存到数据库失败，无法打包导出。");
        try
        {
            _services.Projects.ExportProject(id, zipPath);
            return ProjectOperationResult.Ok(id);
        }
        catch (Exception ex)
        {
            AppLog.Error("[ShellVM] 打包导出项目失败", ex);
            return ProjectOperationResult.Fail("打包导出失败：" + ex.Message);
        }
    }

    /// <summary>导入项目包（*.paproj）并把其中的数据加载到当前会话。</summary>
    public ProjectOperationResult ImportAndLoadProject(string zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath))
            return ProjectOperationResult.Fail("未指定项目包路径。");
        var imported = _services.Projects.ImportProject(zipPath);
        if (!imported.Success || imported.ProjectId is null)
            return imported;
        return _services.Projects.LoadProject(imported.ProjectId);
    }

    /// <summary>项目名：优先用当前视频文件名（去扩展名），无则「未命名项目」。</summary>
    private string ResolveProjectName()
    {
        var path = Player.CurrentPath;
        if (string.IsNullOrWhiteSpace(path)) return "未命名项目";
        var name = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(name) ? "未命名项目" : name;
    }
}
