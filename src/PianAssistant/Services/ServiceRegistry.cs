using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// Aggregates all application services into a single container.
/// Hand-written constructor injection; no DI container package
/// (PianAssistant convention, mirrors StellarHub ServiceRegistry).
/// </summary>
public sealed class ServiceRegistry
{
    public AppSettingsService Settings { get; }
    public IVideoPlayback Playback { get; }
    public ScreenshotService Screenshots { get; }
    public ClipExtractionService Clips { get; }
    public ConcatService Concats { get; }
    public FfmpegRunner Ffmpeg { get; }
    public HotkeyService Hotkeys { get; }
    public ProjectDatabase Database { get; }
    public ProjectRepository Repository { get; }
    public ProjectService Projects { get; }
    public BatchExecutionService Batches { get; }
    /// <summary>素材会话自动保存/恢复服务（M7）。</summary>
    public MediaSessionService Sessions { get; }

    /// <summary>数据库初始化结果（损坏自愈 / 失败时非 null），供启动层友好提示。</summary>
    public DatabaseOpenResult? DatabaseStatus { get; private set; }

    public ServiceRegistry(
        AppSettingsService settings,
        IVideoPlayback playback,
        ScreenshotService screenshots,
        ClipExtractionService clips,
        ConcatService concats,
        FfmpegRunner ffmpeg,
        HotkeyService hotkeys,
        ProjectDatabase database,
        ProjectRepository repository,
        ProjectService projects,
        BatchExecutionService batches,
        MediaSessionService sessions)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Playback = playback ?? throw new ArgumentNullException(nameof(playback));
        Screenshots = screenshots ?? throw new ArgumentNullException(nameof(screenshots));
        Clips = clips ?? throw new ArgumentNullException(nameof(clips));
        Concats = concats ?? throw new ArgumentNullException(nameof(concats));
        Ffmpeg = ffmpeg ?? throw new ArgumentNullException(nameof(ffmpeg));
        Hotkeys = hotkeys ?? throw new ArgumentNullException(nameof(hotkeys));
        Database = database ?? throw new ArgumentNullException(nameof(database));
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Projects = projects ?? throw new ArgumentNullException(nameof(projects));
        Batches = batches ?? throw new ArgumentNullException(nameof(batches));
        Sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    /// <summary>
    /// Production wiring: resolves bundled ffmpeg.exe, constructs services
    /// in the correct order, initializes the SQLite database, and applies
    /// user's hotkey overrides.
    /// </summary>
    public static ServiceRegistry CreateDefault()
    {
        var settings = new AppSettingsService();
        // 日志目录跟随解析后的数据根（M7）。
        AppLog.SetDirectory(settings.LogsDirectory);
        var ffmpeg = FfmpegRunner.FromBundledTools();
        var playback = new VideoPlaybackService();
        var screenshots = new ScreenshotService(
            playback, ffmpeg, settings.ScreenshotsDirectory, settings.SnapshotFormat,
            settings.OrganizeByDate, settings.ProjectSubfolderName);
        var clips = new ClipExtractionService(ffmpeg, settings.ClipsDirectory,
            settings.OrganizeByDate, settings.ProjectSubfolderName);
        var concats = new ConcatService(ffmpeg);
        var batches = new BatchExecutionService(ffmpeg, clips, settings);
        var hotkeys = new HotkeyService();
        foreach (var kv in settings.GetHotkeyOverrides())
            hotkeys.SetCustom(kv.Key, kv.Value);

        // 初始化 SQLite 数据库与仓储（M3 持久化）。
        // 使用 EnsureInitialized：损坏库文件会被隔离为 *.corrupt 并重建空库。
        var database = new ProjectDatabase(settings.DatabasePath);
        var dbStatus = database.EnsureInitialized();
        var repository = new ProjectRepository(database);
        var projects = new ProjectService(database, repository, settings);
        // 会话还原在后台线程算完哈希后需回到 UI 线程应用（M7）。
        var sessions = new MediaSessionService(repository, settings, CreateUiDispatch());

        return new ServiceRegistry(settings, playback, screenshots, clips, concats, ffmpeg,
            hotkeys, database, repository, projects, batches, sessions)
        {
            DatabaseStatus = dbStatus,
        };
    }

    /// <summary>把后台线程回调切回 UI 线程（无 WPF 应用时退化为同步执行，供测试）。</summary>
    private static Action<Action> CreateUiDispatch() => action =>
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }
        try
        {
            dispatcher.Invoke(action);
        }
        catch (Exception ex)
        {
            AppLog.Error("[ServiceRegistry] UI 线程派发失败", ex);
        }
    };
}
