using System.IO;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// ffmpeg 缺失时的友好提示测试：构造指向不存在路径的 FfmpegRunner，
/// 入口方法应抛出含中文脚本指引（download-ffmpeg.ps1）的 FileNotFoundException，
/// 绝不静默失败。
/// </summary>
public class FfmpegRunnerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _missingFfmpeg;
    private readonly string _missingFfprobe;

    public FfmpegRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-ffmpeg-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _missingFfmpeg = Path.Combine(_tempDir, "ffmpeg.exe");
        _missingFfprobe = Path.Combine(_tempDir, "ffprobe.exe");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private FfmpegRunner CreateMissingRunner() => new(_missingFfmpeg, _missingFfprobe);

    [Fact]
    public void CaptureSingleFrame_MissingExe_ThrowsFriendly()
    {
        var runner = CreateMissingRunner();

        var ex = Assert.Throws<FileNotFoundException>(
            () => runner.CaptureSingleFrame("in.mp4", TimeSpan.Zero, "out.png"));

        Assert.Contains("ffmpeg.exe", ex.Message);
        Assert.Contains("download-ffmpeg.ps1", ex.Message);
    }

    [Fact]
    public void ExtractClipStreamCopy_MissingExe_ThrowsFriendly()
    {
        var runner = CreateMissingRunner();

        var ex = Assert.Throws<FileNotFoundException>(
            () => runner.ExtractClipStreamCopy(
                "in.mp4", TimeSpan.Zero, TimeSpan.FromSeconds(1), "out.mp4", CancellationToken.None));

        Assert.Contains("download-ffmpeg.ps1", ex.Message);
    }

    [Fact]
    public void ConcatFiles_MissingExe_ThrowsFriendly()
    {
        var runner = CreateMissingRunner();

        var ex = Assert.Throws<FileNotFoundException>(
            () => runner.ConcatFiles("list.txt", "out.mp4", 1000, CancellationToken.None));

        Assert.Contains("download-ffmpeg.ps1", ex.Message);
    }
}
