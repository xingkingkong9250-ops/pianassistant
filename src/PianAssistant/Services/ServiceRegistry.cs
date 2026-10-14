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
        BatchExecutionService batches)
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
    }

    /// <summary>
    /// Production wiring: resolves bundled ffmpeg.exe, constructs services
    /// in the correct order, initializes the SQLite database, and applies
    /// user's hotkey overrides.
    /// </summary>
    public static ServiceRegistry CreateDefault()
    {
        var settings = new AppSettingsService();
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
        var database = new ProjectDatabase();
        var dbStatus = database.EnsureInitialized();
        var repository = new ProjectRepository(database);
        var projects = new ProjectService(database, repository, settings);

        return new ServiceRegistry(settings, playback, screenshots, clips, concats, ffmpeg,
            hotkeys, database, repository, projects, batches)
        {
            DatabaseStatus = dbStatus,
        };
    }
}
