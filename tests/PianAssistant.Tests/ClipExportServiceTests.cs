using System.IO;
using PianAssistant.Models;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// 裁切点导入/导出测试：导出格式（头注释 + 裁切点行 + 自动片段行）、
/// 导出→导入往返、片段行解析与容错。
/// </summary>
public class ClipExportServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ClipExportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-clipexport-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string TempPath(string name) => Path.Combine(_tempDir, name);

    private static readonly TimeSpan[] SampleCutPoints =
    {
        TimeSpan.FromSeconds(90),   // 00:01:30
        TimeSpan.FromSeconds(260),  // 00:04:20
    };

    // --- 导出格式 ---

    [Fact]
    public void Export_WritesHeaderAndCutPointLines()
    {
        var path = TempPath("points.txt");
        ClipExportService.ExportCutPoints(path, SampleCutPoints, TimeSpan.FromSeconds(430));

        var lines = File.ReadAllLines(path);
        Assert.Equal("# PianAssistant 裁切点导出 v1", lines[0]);
        Assert.StartsWith("# 每行一个裁切点", lines[1]);
        Assert.Contains("00:01:30", lines);
        Assert.Contains("00:04:20", lines);
    }

    [Fact]
    public void Export_GeneratesSegmentLinesBetweenCutPoints()
    {
        var path = TempPath("points.txt");
        // 0 → 1:30 → 4:20 → 7:10（结尾）共 3 段
        ClipExportService.ExportCutPoints(path, SampleCutPoints, TimeSpan.FromSeconds(430));

        var lines = File.ReadAllLines(path);
        Assert.Contains("00:00:00-00:01:30 片段 #1", lines);
        Assert.Contains("00:01:30-00:04:20 片段 #2", lines);
        Assert.Contains("00:04:20-00:07:10 片段 #3", lines);
        // 片段行编号连续
        Assert.Equal(3, lines.Count(l => l.Contains("片段 #")));
    }

    [Fact]
    public void Export_UnsortedAndDuplicatePoints_Normalized()
    {
        var path = TempPath("points.txt");
        var points = new List<TimeSpan>
        {
            TimeSpan.FromSeconds(260),
            TimeSpan.FromSeconds(90),
            TimeSpan.FromSeconds(260), // 重复
        };
        ClipExportService.ExportCutPoints(path, points, TimeSpan.Zero);

        var content = File.ReadAllText(path);
        // 排序后只出现一次 00:04:20 裁切点行
        var cutPointLines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(l => l.Trim() == "00:04:20");
        Assert.Equal(1, cutPointLines);
    }

    [Fact]
    public void Export_NoCutPoints_WithLength_WritesSingleSegment()
    {
        var path = TempPath("points.txt");
        ClipExportService.ExportCutPoints(path, Array.Empty<TimeSpan>(), TimeSpan.FromSeconds(600));
        var lines = File.ReadAllLines(path);
        Assert.Contains("00:00:00-00:10:00 片段 #1", lines);
    }

    // --- 导入 ---

    [Fact]
    public void RoundTrip_ExportThenImport_SameCutPoints()
    {
        var path = TempPath("roundtrip.txt");
        ClipExportService.ExportCutPoints(path, SampleCutPoints, TimeSpan.FromSeconds(430));

        // 传入源视频路径以同时还原片段行
        var (points, segments) = ClipExportService.ImportCutPoints(path, @"C:\v\a.mp4");
        Assert.Equal(
            SampleCutPoints.OrderBy(p => p).Select(p => p.TotalMilliseconds),
            points.Select(p => p.TotalMilliseconds));
        // 片段行也解析出来（3 段）
        Assert.Equal(3, segments.Count);
        Assert.All(segments, s => Assert.Equal(@"C:\v\a.mp4", s.SourcePath));
    }

    [Fact]
    public void Import_WithSourcePath_CreatesClipRanges()
    {
        var path = TempPath("points.txt");
        File.WriteAllLines(path, new[]
        {
            "# 注释",
            string.Empty,
            "00:01:00-00:02:00 片段 开场",
            "00:01:30",
        });

        var (points, segments) = ClipExportService.ImportCutPoints(path, @"C:\v\a.mp4");
        var point = Assert.Single(points);
        Assert.Equal(TimeSpan.FromSeconds(90), point);
        var segment = Assert.Single(segments);
        Assert.Equal(@"C:\v\a.mp4", segment.SourcePath);
        Assert.Equal(TimeSpan.FromMinutes(1), segment.InPoint);
        Assert.Equal(TimeSpan.FromMinutes(2), segment.OutPoint);
        Assert.Equal("开场", segment.Label);
    }

    [Fact]
    public void Import_WithoutSourcePath_SkipsSegments()
    {
        var path = TempPath("points.txt");
        File.WriteAllLines(path, new[] { "00:01:00-00:02:00 片段 开场", "00:01:30" });

        var (points, segments) = ClipExportService.ImportCutPoints(path);
        Assert.Single(points);
        Assert.Empty(segments);
    }

    [Fact]
    public void Import_InvalidAndCommentLines_IgnoredWithoutThrow()
    {
        var path = TempPath("points.txt");
        File.WriteAllLines(path, new[]
        {
            "// 斜杠注释",
            "随便什么乱码行",
            "abc 截图",
            "00:02:00",
        });

        var (points, segments) = ClipExportService.ImportCutPoints(path);
        var point = Assert.Single(points);
        Assert.Equal(TimeSpan.FromMinutes(2), point);
        Assert.Empty(segments);
    }
}
