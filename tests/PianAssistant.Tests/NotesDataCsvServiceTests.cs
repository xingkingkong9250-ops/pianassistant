using System.IO;
using PianAssistant.Models;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>「导出数据」CSV 表格序列化与解析（可人工编辑格式）。</summary>
public class NotesDataCsvServiceTests
{
    [Fact]
    public void Export_WritesHeaderCutPointsSegmentsScreenshots()
    {
        var data = new NotesExportData
        {
            SourcePath = "C:\\v.mp4",
            CutPoints = ["00:00:10.000", "00:05:20.000"],
            Segments =
            [
                new ExportSegment { Index = 1, Start = "00:00:00.000", End = "00:00:10.000", Marked = false },
                new ExportSegment { Index = 2, Start = "00:00:10.000", End = "00:05:20.000", Marked = true },
            ],
            Screenshots =
            [
                new ExportScreenshot { Position = "00:01:23.456", File = "C:\\shots\\a,b.png", Marked = true },
            ],
        };
        var path = Path.Combine(Path.GetTempPath(), $"pia_csv_{Guid.NewGuid():N}.csv");
        try
        {
            NotesDataCsvService.Export(path, data);
            var lines = File.ReadAllLines(path);
            Assert.Equal("类型,开始,结束,标记,文件", lines[0]);
            Assert.Equal("裁切点,00:00:10.000,,,", lines[1]);
            Assert.Equal("片段,00:00:00.000,00:00:10.000,,", lines[3]);
            Assert.Equal("片段,00:00:10.000,00:05:20.000,✓,", lines[4]);
            Assert.Equal("截图,00:01:23.456,,✓,C:\\shots\\a,b.png", lines[5]);
            // UTF-8 BOM（Excel 兼容）
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(0xEF, bytes[0]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Import_RoundTrip_PreservesAllData()
    {
        var data = new NotesExportData
        {
            CutPoints = ["00:00:10.000"],
            Segments = [new ExportSegment { Start = "00:00:00.000", End = "00:00:10.000", Marked = true }],
            Screenshots = [new ExportScreenshot { Position = "00:00:05.000", File = "C:\\s\\x.png", Marked = true }],
        };
        var path = Path.Combine(Path.GetTempPath(), $"pia_csv_{Guid.NewGuid():N}.csv");
        try
        {
            NotesDataCsvService.Export(path, data);
            var parsed = NotesDataCsvService.Import(path);
            Assert.Single(parsed.CutPoints);
            Assert.Equal("00:00:10.000", parsed.CutPoints[0]);
            Assert.Single(parsed.Segments);
            Assert.True(parsed.Segments[0].Marked);
            Assert.Equal("00:00:00.000", parsed.Segments[0].Start);
            Assert.Single(parsed.Screenshots);
            Assert.True(parsed.Screenshots[0].Marked);
            Assert.Equal("C:\\s\\x.png", parsed.Screenshots[0].File);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Import_ToleratesEdits_Comments_LooseTime_LooseMarks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pia_csv_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllLines(path,
            [
                "类型,开始,结束,标记,文件",
                "# 人工注释行",
                "",
                "裁切点,0:00:10,",
                "裁切点,0:00:15,",
                "片段,00:00:00,00:00:10,true,",
                "截图,00:00:05,,√,C:\\a.png",
                "垃圾行,xxx",
            ]);
            var parsed = NotesDataCsvService.Import(path);
            Assert.Equal(2, parsed.CutPoints.Count);       // 宽松时间码均有效
            Assert.Equal("00:00:10.000", parsed.CutPoints[0]);
            Assert.Equal("00:00:15.000", parsed.CutPoints[1]);
            Assert.Single(parsed.Segments);
            Assert.True(parsed.Segments[0].Marked);        // true
            Assert.Single(parsed.Screenshots);
            Assert.True(parsed.Screenshots[0].Marked);     // √
            Assert.Equal("C:\\a.png", parsed.Screenshots[0].File);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Import_SortsAndDedupesCutPoints()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pia_csv_{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllLines(path,
            [
                "类型,开始,结束,标记,文件",
                "裁切点,00:00:30.000,,,",
                "裁切点,00:00:10.000,,,",
                "裁切点,00:00:30.000,,,",
                "裁切点,00:00:00.000,,,",          // 零点被丢弃
            ]);
            var parsed = NotesDataCsvService.Import(path);
            Assert.Equal(2, parsed.CutPoints.Count);
            Assert.Equal("00:00:10.000", parsed.CutPoints[0]);
            Assert.Equal("00:00:30.000", parsed.CutPoints[1]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
