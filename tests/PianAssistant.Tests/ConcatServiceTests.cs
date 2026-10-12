using System.Diagnostics;
using System.IO;
using System.Text;
using PianAssistant.Models;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

public class ConcatServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FfmpegRunner _ffmpeg;
    private readonly ConcatService _svc;

    public ConcatServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-concat-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        // 桩 ffmpeg（不会真正被调用，仅满足构造）
        var ffmpegPath = Path.Combine(_tempDir, "ffmpeg.exe");
        var ffprobePath = Path.Combine(_tempDir, "ffprobe.exe");
        File.WriteAllText(ffmpegPath, "stub");
        File.WriteAllText(ffprobePath, "stub");
        _ffmpeg = new FfmpegRunner(ffmpegPath, ffprobePath);
        _svc = new ConcatService(_ffmpeg);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // --- list.txt 内容格式 ---

    [Fact]
    public void BuildListFileContent_SingleFile_HasFileDirective()
    {
        var content = ConcatService.BuildListFileContent(new[] { @"C:\tmp\a.mp4" });
        Assert.Equal("file 'C:\\tmp\\a.mp4'" + Environment.NewLine, content);
    }

    [Fact]
    public void BuildListFileContent_MultipleFiles_OneDirectivePerLine()
    {
        var content = ConcatService.BuildListFileContent(
            new[] { @"C:\tmp\a.mp4", @"C:\tmp\b.mp4", @"C:\tmp\c.mp4" });
        var lines = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.All(lines, l => Assert.StartsWith("file '", l));
        Assert.EndsWith("'", lines[2]);
        Assert.Contains("b.mp4", lines[1]);
    }

    [Fact]
    public void BuildListFileContent_EscapesSingleQuotes()
    {
        // ffmpeg concat 清单中单引号转义为 '\''
        var content = ConcatService.BuildListFileContent(new[] { @"C:\tmp\it's.mp4" });
        Assert.Contains(@"it'\''s.mp4", content);
    }

    // --- 参数校验 ---

    [Fact]
    public async Task ConcatAsync_EmptyList_Throws()
    {
        var output = Path.Combine(_tempDir, "out.mp4");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.ConcatAsync(new List<ClipRange>(), output, CancellationToken.None));
    }

    [Fact]
    public async Task ConcatAsync_DifferentSources_Throws()
    {
        var ranges = new List<ClipRange>
        {
            new(@"C:\v\a.mp4", TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new(@"C:\v\b.mp4", TimeSpan.Zero, TimeSpan.FromSeconds(1)),
        };
        var output = Path.Combine(_tempDir, "out.mp4");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.ConcatAsync(ranges, output, CancellationToken.None));
        Assert.Contains("同一源视频", ex.Message);
    }

    [Fact]
    public async Task ConcatAsync_IncompleteClip_Throws()
    {
        var ranges = new List<ClipRange>
        {
            new(@"C:\v\a.mp4", TimeSpan.Zero), // 无出点
            new(@"C:\v\a.mp4", TimeSpan.Zero, TimeSpan.FromSeconds(1)),
        };
        var output = Path.Combine(_tempDir, "out.mp4");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.ConcatAsync(ranges, output, CancellationToken.None));
    }

    // --- 真实 ffmpeg 执行（SkipIfMissing：未下载 ffmpeg 时跳过）---

    [Fact]
    public async Task ConcatAsync_RealFfmpeg_ProducesOutputFile()
    {
        // SkipIfMissing：仓库未下载 ffmpeg.exe 时直接跳过
        var ffmpegPath = FindBundledFfmpeg();
        if (ffmpegPath is null) return;
        var ffprobePath = Path.Combine(Path.GetDirectoryName(ffmpegPath)!, "ffprobe.exe");
        if (!File.Exists(ffprobePath)) return;

        var workDir = Path.Combine(_tempDir, "real");
        Directory.CreateDirectory(workDir);
        var runner = new FfmpegRunner(ffmpegPath, ffprobePath);

        // 用 lavfi 生成 2 秒测试视频；构建不含 lavfi 时同样跳过
        var source = Path.Combine(workDir, "source.mp4");
        if (!TryCreateTestVideo(ffmpegPath, source))
            return;

        var clips = new ClipExtractionService(runner, workDir);
        var concat = new ConcatService(runner);
        var ranges = new List<ClipRange>
        {
            new(source, TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new(source, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)),
        };
        var output = Path.Combine(workDir, "joined.mp4");
        var progress = new List<double>();

        await concat.ConcatAsync(ranges, output, CancellationToken.None, progress.Add);

        Assert.True(File.Exists(output), "拼接输出文件应存在");
        Assert.True(new FileInfo(output).Length > 0, "拼接输出文件不应为空");
        // 进度回调最终到达 1
        Assert.True(progress.Count > 0);
        Assert.Equal(1.0, progress[^1]);
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
