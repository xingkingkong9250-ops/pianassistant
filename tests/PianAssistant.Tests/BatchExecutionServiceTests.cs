using System.Diagnostics;
using System.IO;
using PianAssistant.Models;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// 批量指令执行器测试：标记/裁切点跳过逻辑无需 ffmpeg；
/// 截图/片段用真实 ffmpeg（SkipIfMissing：未下载 ffmpeg 时跳过），
/// 参照 ConcatServiceTests 模式。
/// </summary>
public class BatchExecutionServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FfmpegRunner _ffmpeg;
    private readonly ClipExtractionService _clips;
    private readonly AppSettingsService _settings;
    private readonly BatchExecutionService _svc;

    public BatchExecutionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-batch-exec-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        // 桩 ffmpeg（Marker/CutPoint 测试不会真正调用，仅满足构造）
        var ffmpegPath = Path.Combine(_tempDir, "ffmpeg.exe");
        var ffprobePath = Path.Combine(_tempDir, "ffprobe.exe");
        File.WriteAllText(ffmpegPath, "stub");
        File.WriteAllText(ffprobePath, "stub");
        _ffmpeg = new FfmpegRunner(ffmpegPath, ffprobePath);
        _clips = new ClipExtractionService(_ffmpeg, _tempDir);
        // 设置服务用临时目录作为数据根，截图/片段目录从数据根派生（避免写用户 AppData）
        _settings = new AppSettingsService(Path.Combine(_tempDir, "settings"));
        _settings.SetDataRoot(_tempDir);
        _svc = new BatchExecutionService(_ffmpeg, _clips, _settings);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private static BatchInstruction Marker(int line = 1) =>
        BatchInstruction.Valid(BatchActionKind.Marker, TimeSpan.FromSeconds(10), null, "转场", line);

    private static BatchInstruction CutPoint(int line = 1) =>
        BatchInstruction.Valid(BatchActionKind.CutPoint, TimeSpan.FromSeconds(20), null, null, line);

    // --- 跳过型指令（裁切点：由 VM 层分发，执行器标成功；标记已不支持） ---

    [Fact]
    public async Task ExecuteAsync_MarkerInstruction_FailsNotSupported()
    {
        var (results, done, total) = await RunAsync(new[] { Marker() }, "stub.mp4");
        Assert.Equal(1, done);
        Assert.Equal(1, total);
        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.Contains("不支持", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_CutPointInstruction_SucceedsWithoutOutput()
    {
        var (results, _, _) = await RunAsync(new[] { CutPoint() }, "stub.mp4");
        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Null(result.OutputPath);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidInstruction_MarkedFailed()
    {
        var invalid = BatchInstruction.Invalid(3, "第 3 行：无法识别的时间「abc」");
        var (results, _, _) = await RunAsync(new[] { invalid }, "stub.mp4");
        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.Contains("3", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_PreCancelled_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _svc.ExecuteAsync(new[] { Marker() }, "stub.mp4", cts.Token, (_, _, _, _) => { }));
    }

    [Fact]
    public async Task ExecuteAsync_EmptyList_CompletesWithoutCallback()
    {
        var called = 0;
        await _svc.ExecuteAsync(Array.Empty<BatchInstruction>(), "stub.mp4",
            CancellationToken.None, (_, _, _, _) => called++);
        Assert.Equal(0, called);
    }

    // --- 真实 ffmpeg 执行（SkipIfMissing：未下载 ffmpeg 时跳过） ---

    [Fact]
    public async Task ExecuteAsync_Screenshot_RealFfmpeg_ProducesPng()
    {
        // SkipIfMissing：仓库未下载 ffmpeg.exe 时直接跳过
        var ffmpegPath = FindBundledFfmpeg();
        if (ffmpegPath is null) return;

        var workDir = Path.Combine(_tempDir, "real-ss");
        Directory.CreateDirectory(workDir);
        var runner = new FfmpegRunner(ffmpegPath,
            Path.Combine(Path.GetDirectoryName(ffmpegPath)!, "ffprobe.exe"));

        // 用 lavfi 生成 2 秒测试视频；构建不含 lavfi 时同样跳过
        var source = Path.Combine(workDir, "source.mp4");
        if (!TryCreateTestVideo(ffmpegPath, source)) return;

        var settings = new AppSettingsService(Path.Combine(workDir, "settings"));
        settings.SetDataRoot(workDir);
        settings.SetOrganizationOptions(false, null);
        var svc = new BatchExecutionService(runner, new ClipExtractionService(runner, workDir), settings);

        var instruction = BatchInstruction.Valid(BatchActionKind.Screenshot,
            TimeSpan.FromSeconds(1), null, "测试", 1);
        var (results, done, _) = await RunAsync(svc, new[] { instruction }, source);

        Assert.Equal(1, done);
        var result = Assert.Single(results);
        Assert.True(result.Success, $"截图失败：{result.Error}");
        Assert.NotNull(result.OutputPath);
        Assert.True(File.Exists(result.OutputPath), "截图输出文件应存在");
        Assert.True(new FileInfo(result.OutputPath!).Length > 0, "截图输出文件不应为空");
    }

    [Fact]
    public async Task ExecuteAsync_Clip_RealFfmpeg_ProducesOutputFile()
    {
        // SkipIfMissing：仓库未下载 ffmpeg.exe 时直接跳过
        var ffmpegPath = FindBundledFfmpeg();
        if (ffmpegPath is null) return;

        var workDir = Path.Combine(_tempDir, "real-clip");
        Directory.CreateDirectory(workDir);
        var runner = new FfmpegRunner(ffmpegPath,
            Path.Combine(Path.GetDirectoryName(ffmpegPath)!, "ffprobe.exe"));

        var source = Path.Combine(workDir, "source.mp4");
        if (!TryCreateTestVideo(ffmpegPath, source)) return;

        var settings = new AppSettingsService(Path.Combine(workDir, "settings"));
        settings.SetDataRoot(workDir);
        settings.SetOrganizationOptions(false, null);
        var svc = new BatchExecutionService(runner, new ClipExtractionService(runner, workDir), settings);

        var instruction = BatchInstruction.Valid(BatchActionKind.Clip,
            TimeSpan.Zero, TimeSpan.FromSeconds(1), "片段A", 1);
        var (results, _, _) = await RunAsync(svc, new[] { instruction }, source);

        var result = Assert.Single(results);
        Assert.True(result.Success, $"片段失败：{result.Error}");
        Assert.NotNull(result.OutputPath);
        Assert.True(File.Exists(result.OutputPath), "片段输出文件应存在");
        Assert.True(new FileInfo(result.OutputPath!).Length > 0, "片段输出文件不应为空");
    }

    [Fact]
    public async Task ExecuteAsync_Screenshot_NonexistentVideo_FailsGracefully()
    {
        // SkipIfMissing：需要真实 ffmpeg 才能触发失败路径
        var ffmpegPath = FindBundledFfmpeg();
        if (ffmpegPath is null) return;

        var workDir = Path.Combine(_tempDir, "fail-ss");
        Directory.CreateDirectory(workDir);
        var runner = new FfmpegRunner(ffmpegPath,
            Path.Combine(Path.GetDirectoryName(ffmpegPath)!, "ffprobe.exe"));
        var settings = new AppSettingsService(Path.Combine(workDir, "settings"));
        settings.SetDataRoot(workDir);
        settings.SetOrganizationOptions(false, null);
        var svc = new BatchExecutionService(runner, new ClipExtractionService(runner, workDir), settings);

        var instruction = BatchInstruction.Valid(BatchActionKind.Screenshot,
            TimeSpan.FromSeconds(1), null, null, 1);
        var (results, _, _) = await RunAsync(svc, new[] { instruction },
            Path.Combine(workDir, "不存在.mp4"));

        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    // --- 辅助 ---

    /// <summary>用默认服务跑一批指令，收集每条结果与回调计数。</summary>
    private async Task<(List<BatchResult> Results, int Done, int Total)> RunAsync(
        IReadOnlyList<BatchInstruction> instructions, string videoPath)
    {
        return await RunAsync(_svc, instructions, videoPath);
    }

    private static async Task<(List<BatchResult> Results, int Done, int Total)> RunAsync(
        BatchExecutionService svc, IReadOnlyList<BatchInstruction> instructions, string videoPath)
    {
        var results = new List<BatchResult>();
        var done = 0;
        var total = 0;
        await svc.ExecuteAsync(instructions, videoPath, CancellationToken.None,
            (d, t, _, r) => { done = d; total = t; results.Add(r); });
        return (results, done, total);
    }

    /// <summary>从测试程序集目录向上定位仓库内捆绑的 ffmpeg.exe。</summary>
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

    /// <summary>用 lavfi testsrc 生成测试视频；失败（不支持 lavfi/超时）返回 false。</summary>
    private static bool TryCreateTestVideo(string ffmpegPath, string outputPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-f lavfi -i testsrc=duration=2:size=128x96:rate=15 " +
                            "-pix_fmt yuv420p -y \"" + outputPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi)!;
            if (!process.WaitForExit(30000)) return false;
            return process.ExitCode == 0 && File.Exists(outputPath);
        }
        catch
        {
            return false;
        }
    }
}
