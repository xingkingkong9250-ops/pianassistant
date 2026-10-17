using System.IO;
using ClosedXML.Excel;
using PianAssistant.Models;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>「导出数据」xlsx 工作簿序列化与解析（可人工编辑格式）。</summary>
public class NotesDataXlsxServiceTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"pia_xlsx_{Guid.NewGuid():N}.xlsx");

    [Fact]
    public void Export_WritesHeaderAndRows()
    {
        var data = new NotesExportData
        {
            SourcePath = "C:\\v.mp4",
            CutPoints = ["00:00:10.000", "00:05:20.000"],
            Segments = [new ExportSegment { Start = "00:00:00.000", End = "00:00:10.000", Marked = true }],
            Screenshots = [new ExportScreenshot { Position = "00:01:23.456", File = "C:\\shots\\a.png", Marked = false }],
        };
        var path = TempPath();
        try
        {
            NotesDataXlsxService.Export(path, data);
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet(1);
            Assert.Equal("类型", ws.Cell(1, 1).GetString());
            Assert.Equal("开始", ws.Cell(1, 2).GetString());
            Assert.Equal("裁切点", ws.Cell(2, 1).GetString());
            Assert.Equal("00:00:10.000", ws.Cell(2, 2).GetString());
            Assert.Equal("片段", ws.Cell(4, 1).GetString());
            Assert.True(ws.Cell(4, 4).GetBoolean());            // 标记列为布尔
            Assert.Equal("截图", ws.Cell(5, 1).GetString());
            Assert.Equal("C:\\shots\\a.png", ws.Cell(5, 5).GetString());
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
            Screenshots = [new ExportScreenshot { Position = "00:00:05.000", File = "C:\\s\\x.png", Marked = false }],
        };
        var path = TempPath();
        try
        {
            NotesDataXlsxService.Export(path, data);
            var parsed = NotesDataXlsxService.Import(path);
            Assert.Single(parsed.CutPoints);
            Assert.Equal("00:00:10.000", parsed.CutPoints[0]);
            Assert.Single(parsed.Segments);
            Assert.True(parsed.Segments[0].Marked);
            Assert.Single(parsed.Screenshots);
            Assert.False(parsed.Screenshots[0].Marked);
            Assert.Equal("C:\\s\\x.png", parsed.Screenshots[0].File);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Import_ToleratesManualEdits_TextMarks_LooseTime()
    {
        var path = TempPath();
        try
        {
            // 模拟人工编辑：手写工作簿（标记用文本 ✓/1，时间码用 0:00:10）
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("拉片数据");
                ws.Cell(1, 1).Value = "类型";
                ws.Cell(2, 1).Value = "裁切点";
                ws.Cell(2, 2).Value = "0:00:10";
                ws.Cell(3, 1).Value = "片段";
                ws.Cell(3, 2).Value = "00:00:00";
                ws.Cell(3, 3).Value = "00:00:10";
                ws.Cell(3, 4).Value = "✓";
                ws.Cell(4, 1).Value = "截图";
                ws.Cell(4, 2).Value = "00:00:05";
                ws.Cell(4, 4).Value = "1";
                ws.Cell(4, 5).Value = "C:\\a.png";
                wb.SaveAs(path);
            }
            var parsed = NotesDataXlsxService.Import(path);
            Assert.Single(parsed.CutPoints);
            Assert.Equal("00:00:10.000", parsed.CutPoints[0]);
            Assert.Single(parsed.Segments);
            Assert.True(parsed.Segments[0].Marked);             // 文本 ✓
            Assert.Single(parsed.Screenshots);
            Assert.True(parsed.Screenshots[0].Marked);          // 文本 1
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
