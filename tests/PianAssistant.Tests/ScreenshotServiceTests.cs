using System.IO;
using PianAssistant.Models;
using PianAssistant.Services;
using PianAssistant.Tests.Stubs;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// 截图服务测试（v0.20.0 起为 ffmpeg 后台抽帧，不再用播放器截屏）：
/// Capture 会真正执行 ffmpeg，因此需要捆绑的真实 ffmpeg 与 lavfi 生成的
/// 可解码视频；二者任一缺失则跳过（SkipIfMissing 模式，参照 BatchExecutionServiceTests）。
/// </summary>
public class ScreenshotServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StubVideoPlayback _playback;
    private readonly FfmpegRunner _ffmpeg;
    private readonly bool _ready; // 真实 ffmpeg + lavfi 测试视频就绪

    public ScreenshotServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-screenshot-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _playback = new StubVideoPlayback { Length = TimeSpan.FromMinutes(3) };

        var real = FindBundledFfmpeg();
        if (real is null)
        {
            var stubPath = Path.Combine(_tempDir, "ffmpeg.exe");
            File.WriteAllText(stubPath, "stub");
            _ffmpeg = new FfmpegRunner(stubPath, Path.Combine(_tempDir, "ffprobe.exe"));
            return;
        }
        _ffmpeg = new FfmpegRunner(real,
            Path.Combine(Path.GetDirectoryName(real)!, "ffprobe.exe"));

        // lavfi 生成 2 秒可解码测试视频；桩播放器指向它（Capture 真抽帧）
        var source = TryCreateTestVideo(real, Path.Combine(_tempDir, "test_video.mp4"));
        if (source is null) return;
        _playback.Open(source);
        _ready = true;
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private static string? FindBundledFfmpeg()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "PianAssistant", "Tools", "ffmpeg", "ffmpeg.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>用 lavfi testsrc 生成测试视频；失败（不支持 lavfi/超时）返回 null。</summary>
    private static string? TryCreateTestVideo(string ffmpegPath, string outputPath)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-f lavfi -i testsrc=duration=2:size=320x240:rate=10 -y \"{outputPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(30000);
            return p is { HasExited: true, ExitCode: 0 } && File.Exists(outputPath) ? outputPath : null;
        }
        catch
        {
            return null;
        }
    }

    [Fact]
    public void Capture_ReturnsScreenshotEntry_WithCorrectSource()
    {
        if (!_ready) return; // SkipIfMissing
        var svc = new ScreenshotService(_playback, _ffmpeg, _tempDir, "png");
        var entry = svc.Capture();
        Assert.Equal(_playback.CurrentPath, entry.SourcePath);
        Assert.Equal(".png", Path.GetExtension(entry.OutputPath));
        Assert.True(File.Exists(entry.OutputPath), "抽帧产物应存在");
        Assert.True(new FileInfo(entry.OutputPath).Length > 0, "抽帧产物不应为空");
    }

    [Fact]
    public void Capture_FileNameContainsSourceAndPosition()
    {
        if (!_ready) return;
        _playback.Seek(TimeSpan.FromSeconds(1));
        var svc = new ScreenshotService(_playback, _ffmpeg, _tempDir, "png");
        var entry = svc.Capture();
        // 文件名含源名与时间码片段（HHMMSSfff）
        Assert.Contains("test_video", Path.GetFileName(entry.OutputPath));
    }

    [Fact]
    public void Capture_JpgFormat_UsesJpgExtension()
    {
        if (!_ready) return;
        var svc = new ScreenshotService(_playback, _ffmpeg, _tempDir, "jpg");
        var entry = svc.Capture();
        Assert.Equal(".jpg", Path.GetExtension(entry.OutputPath));
    }

    [Fact]
    public void Capture_AvoidsFileNameCollision()
    {
        if (!_ready) return;
        var svc = new ScreenshotService(_playback, _ffmpeg, _tempDir, "png");
        var entry1 = svc.Capture();
        var entry2 = svc.Capture();
        Assert.NotEqual(entry1.OutputPath, entry2.OutputPath);
    }

    [Fact]
    public void Capture_NoMediaOpen_Throws()
    {
        // 不执行 ffmpeg，无需 _ready
        var emptyPlayback = new StubVideoPlayback();
        var svc = new ScreenshotService(emptyPlayback, _ffmpeg, _tempDir, "png");
        Assert.Throws<InvalidOperationException>(() => svc.Capture());
    }
}
