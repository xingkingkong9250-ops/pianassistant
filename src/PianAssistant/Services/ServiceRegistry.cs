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
    public FfmpegRunner Ffmpeg { get; }
    public HotkeyService Hotkeys { get; }

    public ServiceRegistry(
        AppSettingsService settings,
        IVideoPlayback playback,
        ScreenshotService screenshots,
        ClipExtractionService clips,
        FfmpegRunner ffmpeg,
        HotkeyService hotkeys)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Playback = playback ?? throw new ArgumentNullException(nameof(playback));
        Screenshots = screenshots ?? throw new ArgumentNullException(nameof(screenshots));
        Clips = clips ?? throw new ArgumentNullException(nameof(clips));
        Ffmpeg = ffmpeg ?? throw new ArgumentNullException(nameof(ffmpeg));
        Hotkeys = hotkeys ?? throw new ArgumentNullException(nameof(hotkeys));
    }

    /// <summary>
    /// Production wiring: resolves bundled ffmpeg.exe, constructs services
    /// in the correct order, and applies user's hotkey overrides.
    /// </summary>
    public static ServiceRegistry CreateDefault()
    {
        var settings = new AppSettingsService();
        var ffmpeg = FfmpegRunner.FromBundledTools();
        var playback = new VideoPlaybackService();
        var screenshots = new ScreenshotService(
            playback, ffmpeg, settings.ScreenshotsDirectory, settings.SnapshotFormat);
        var clips = new ClipExtractionService(ffmpeg, settings.ClipsDirectory);
        var hotkeys = new HotkeyService();
        foreach (var kv in settings.GetHotkeyOverrides())
            hotkeys.SetCustom(kv.Key, kv.Value);

        return new ServiceRegistry(settings, playback, screenshots, clips, ffmpeg, hotkeys);
    }
}
