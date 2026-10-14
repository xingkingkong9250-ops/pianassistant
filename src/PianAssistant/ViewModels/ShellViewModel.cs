using System.Collections.Specialized;
using System.IO;
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
    private SidebarPanel _activePanel = SidebarPanel.Playlist;
    private bool _isFullscreen;
    private bool _disposed;

    public ServiceRegistry Services => _services;
    public PlayerViewModel Player { get; }
    public PlaylistViewModel Playlist { get; }
    public NotesViewModel Notes { get; }
    public ClipListViewModel Clips { get; }
    public ScreenshotGalleryViewModel Screenshots { get; }
    public SettingsViewModel Settings { get; }
    /// <summary>批量文本操作预览窗 VM（M5）。</summary>
    public BatchPreviewViewModel Batch { get; }

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
        SidebarPanel.Playlist => "播放列表",
        SidebarPanel.Notes => "笔记",
        SidebarPanel.Clips => "📦 片段导出",
        _ => string.Empty,
    };

    public bool IsFullscreen
    {
        get => _isFullscreen;
        set => SetProperty(ref _isFullscreen, value);
    }

    // --- Commands ---

    public ICommand OpenFileCommand { get; }
    public ICommand ToggleSidebarPanelCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand ShowPlaylistCommand { get; }
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
            () => services.Settings.MaxRate);
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

        // Wire Player → panel VMs + Notes 活动流.
        Player.ScreenshotCaptured += (_, entry) =>
        {
            Screenshots.Add(entry);
            Notes.AddScreenshot(entry);
        };
        Player.ClipExtracted += (_, e) =>
        {
            Clips.Add(e.Range);
            Notes.AddClip(e.Range, e.OutputPath);
        };
        Player.SegmentExported += (_, e) =>
        {
            Clips.Add(e.Range);
            Notes.AddClip(e.Range, e.OutputPath);
        };

        // 裁切点变化 → Notes 活动流
        Player.CutPoints.CollectionChanged += OnCutPointsChanged;

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

        // 批量文本操作（M5）：标记/裁切点经回调分发到笔记与播放器；视频路径取当前媒体。
        Batch = new BatchPreviewViewModel(
            new BatchTextParser(),
            services.Batches,
            (text, position) => Notes.AddMarkerAt(text, position),
            position => Player.AddCutPointAt(position),
            () => Player.CurrentPath);

        // 项目服务会话钩子（M6）：保存时把会话数据桥接到持久化层；加载时反向应用到会话。
        WireProjectService(services);

        // Commands.
        OpenFileCommand = new RelayCommand(RequestOpenFile);
        ToggleSidebarPanelCommand = new RelayCommand(
            () => ActivePanel = ActivePanel == SidebarPanel.Playlist
                ? SidebarPanel.Notes
                : SidebarPanel.Playlist);
        ShowPlaylistCommand = new RelayCommand(
            () => ActivePanel = SidebarPanel.Playlist);
        ShowNotesCommand = new RelayCommand(
            () => ActivePanel = SidebarPanel.Notes);
        ShowClipsCommand = new RelayCommand(
            () => ActivePanel = SidebarPanel.Clips);
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
            Player.Open(media.Path);
            _services.Settings.AddRecentFile(media.Path);
            // 打开新视频时清空截图和段
            Notes.ClearAll();
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

    /// <summary>裁切点集合变化时同步到 Notes 活动流（仅添加，不删除历史）。</summary>
    private void OnCutPointsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            foreach (TimeSpan cp in e.NewItems)
                Notes.AddCutPoint(cp);
        }
        // 重新计算段
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Player.Dispose();
        if (_ownsServices)
            (_services.Playback as IDisposable)?.Dispose();
    }

    // --- 项目文件（M6 全功能整合）---

    /// <summary>
    /// 把 ProjectService 的会话访问器接到当前 VM：保存时汇总视频路径/截图/片段/笔记；
    /// 加载时把读回的数据还原到播放列表、截图列表、片段队列与笔记活动流。
    /// </summary>
    private void WireProjectService(ServiceRegistry services)
    {
        services.Projects.ResolveSourcePath = () => Player.CurrentPath;
        services.Projects.ResolveScreenshots = () => Screenshots.Items.ToList();
        services.Projects.ResolveClips = () =>
            Clips.Items
                .Select(clip => (clip, FindClipOutputPath(clip)))
                .ToList();
        services.Projects.ResolveNotes = () =>
        {
            var source = Player.CurrentPath ?? "unknown";
            return Notes.Activities
                .Where(a => a.Type == ActivityType.Note)
                .Select(a => new NoteEntry(source, a.Position, a.Label))
                .ToList();
        };

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
                Notes.AddClip(range, outputPath);
            }
        };
        services.Projects.ApplyNotes = list =>
        {
            foreach (var note in list)
                Notes.AddMarkerAt(note.Text, note.Position);
        };
    }

    /// <summary>按片段入点从活动流中回查其输出路径（活动流片段条目位置=入点）。</summary>
    private string? FindClipOutputPath(ClipRange clip) =>
        Notes.Activities
            .FirstOrDefault(a => a.Type == ActivityType.Clip && a.Position == clip.InPoint)?
            .FilePath;

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
