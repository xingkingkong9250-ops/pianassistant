using System.Collections.Specialized;
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

    // --- Events ---

    public event EventHandler<string>? OpenFileRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? SettingsRequested;
    /// <summary>打开截图画廊窗口（View 层负责创建/聚焦窗口）。</summary>
    public event EventHandler? GalleryRequested;

    public ShellViewModel(ServiceRegistry services, bool ownsServices = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _ownsServices = ownsServices;

        Player = new PlayerViewModel(
            services.Playback, services.Screenshots, services.Clips,
            () => services.Settings.SeekStepFrames,
            () => services.Settings.RateStep);
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
        try
        {
            Player.Open(media.Path);
            _services.Settings.AddRecentFile(media.Path);
            // 打开新视频时清空截图和段
            Notes.ClearAll();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ShellVM] Open media failed: {ex.Message}");
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Player.Dispose();
        if (_ownsServices)
            (_services.Playback as IDisposable)?.Dispose();
    }
}
