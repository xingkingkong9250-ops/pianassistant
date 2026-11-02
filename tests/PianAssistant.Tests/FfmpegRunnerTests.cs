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

    // --- v1.15.7 精准截图：单帧命令构造 ---

    [Fact]
    public void BuildCaptureFrameArgs_ZeroStartTime_UsesPlainInputSeek()
    {
        var args = FfmpegRunner.BuildCaptureFrameArgs(
            "src.mp4", TimeSpan.FromSeconds(6.5), "out.png", 0, null);

        Assert.StartsWith("-ss 6.5 -i \"src.mp4\" ", args);
        Assert.DoesNotContain("-copyts", args);
    }

    [Fact]
    public void BuildCaptureFrameArgs_NonZeroStartTime_UsesCopyTsAbsoluteSeek()
    {
        // mpegts 起始时间 1.466667s：输入级 -ss 越界，必须按绝对时间戳 6.5+1.467 选帧
        var args = FfmpegRunner.BuildCaptureFrameArgs(
            "src.ts", TimeSpan.FromSeconds(6.5), "out.png", 1.466667, null);

        Assert.Contains("-ss 0 -i \"src.ts\" ", args);   // 预卷（位置不足 8s 时从 0 开始）
        Assert.Contains("-copyts ", args);
        Assert.Contains("-ss 7.966 ", args); // 向下取整：避免跳过恰在帧边界的目标帧
    }

    [Fact]
    public void BuildCaptureFrameArgs_NonZeroStartTime_PreRollKeepsSeekFast()
    {
        var args = FfmpegRunner.BuildCaptureFrameArgs(
            "src.ts", TimeSpan.FromSeconds(60), "out.png", 1.5, null);

        Assert.Contains("-ss 52 -i \"src.ts\" ", args); // 60 - 8
        Assert.Contains("-ss 61.5 ", args);         // 60 + 1.5
    }

    [Fact]
    public void BuildCaptureFrameArgs_NegativeStartTimeClamped_FallsBackToPlainSeek()
    {
        var args = FfmpegRunner.BuildCaptureFrameArgs(
            "src.mp4", TimeSpan.FromMilliseconds(100), "out.png", -1.0, null);

        Assert.Contains("-ss 0.1 -i \"src.mp4\" ", args);
        Assert.DoesNotContain("-copyts", args);
    }

    [Fact]
    public void BuildCaptureFrameArgs_KeepsScaleFilterAndImageOptions()
    {
        var args = FfmpegRunner.BuildCaptureFrameArgs(
            "src.mkv", TimeSpan.FromSeconds(3), "out.jpg", 0, "scale=1920:-2");

        Assert.Contains("-vf \"scale=1920:-2\"", args);
        Assert.Contains("-q:v 2", args);
        Assert.Contains("-frames:v 1", args);
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
