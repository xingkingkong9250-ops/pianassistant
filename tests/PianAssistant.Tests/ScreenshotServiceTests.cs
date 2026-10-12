using System.IO;
using PianAssistant.Models;
using PianAssistant.Services;
using PianAssistant.Tests.Stubs;
using Xunit;

namespace PianAssistant.Tests;

public class ScreenshotServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StubVideoPlayback _playback;
    private readonly FfmpegRunner _ffmpeg;

    public ScreenshotServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-screenshot-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _playback = new StubVideoPlayback { Length = TimeSpan.FromMinutes(3) };
        _playback.Open(Path.Combine(_tempDir, "test_video.mp4"));
        // Create a fake ffmpeg.exe so FromBundledTools doesn't throw —
        // but we won't call it because TakeSnapshot succeeds in the stub.
        // For the fallback path, we test directly with FfmpegRunner.
        var ffmpegPath = Path.Combine(_tempDir, "ffmpeg.exe");
        var ffprobePath = Path.Combine(_tempDir, "ffprobe.exe");
        File.WriteAllText(ffmpegPath, "stub");
        File.WriteAllText(ffprobePath, "stub");
        _ffmpeg = new FfmpegRunner(ffmpegPath, ffprobePath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void Capture_ReturnsScreenshotEntry_WithCorrectSource()
    {
        var svc = new ScreenshotService(_playback, _ffmpeg, _tempDir, "png");
        var entry = svc.Capture();
        Assert.Equal(_playback.CurrentPath, entry.SourcePath);
        Assert.True(File.Exists(entry.OutputPath));
        Assert.Equal(".png", Path.GetExtension(entry.OutputPath));
    }

    [Fact]
    public void Capture_FileNameContainsSourceAndPosition()
    {
        _playback.Seek(TimeSpan.FromSeconds(42.5));
        var svc = new ScreenshotService(_playback, _ffmpeg, _tempDir, "png");
        var entry = svc.Capture();
        Assert.Contains("test_video", entry.OutputPath);
        // Position stamp should be in the filename (HHMMSSfff format).
        var fileName = Path.GetFileNameWithoutExtension(entry.OutputPath);
        Assert.Contains("00", fileName); // at least hour/min placeholders
    }

    [Fact]
    public void Capture_JpgFormat_UsesJpgExtension()
    {
        var svc = new ScreenshotService(_playback, _ffmpeg, _tempDir, "jpg");
        var entry = svc.Capture();
        Assert.Equal(".jpg", Path.GetExtension(entry.OutputPath));
    }

    [Fact]
    public void Capture_AvoidsFileNameCollision()
    {
        var svc = new ScreenshotService(_playback, _ffmpeg, _tempDir, "png");
        var entry1 = svc.Capture();
        var entry2 = svc.Capture();
        Assert.NotEqual(entry1.OutputPath, entry2.OutputPath);
    }

    [Fact]
    public void Capture_NoMediaOpen_Throws()
    {
        var emptyPlayback = new StubVideoPlayback();
        var svc = new ScreenshotService(emptyPlayback, _ffmpeg, _tempDir, "png");
        Assert.Throws<InvalidOperationException>(() => svc.Capture());
    }
}
