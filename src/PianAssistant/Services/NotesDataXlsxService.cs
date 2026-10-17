using System.IO;
using ClosedXML.Excel;
using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// 「导出数据」xlsx 工作簿序列化（W1-C2-D-02）：与 CSV 相同的表格结构
/// （类型/开始/结束/标记/文件），标记列为布尔值（Excel 显示 TRUE/FALSE，
/// 直接改值即可），人工编辑后可再导入覆盖。
/// </summary>
public static class NotesDataXlsxService
{
    private const string SheetName = "拉片数据";
    private static readonly string[] Header = ["类型", "开始", "结束", "标记", "文件"];

    /// <summary>导出为 xlsx 工作簿（标记列为布尔值）。</summary>
    public static void Export(string path, NotesExportData data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(data);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName);
        for (var c = 0; c < Header.Length; c++)
            ws.Cell(1, c + 1).Value = Header[c];

        var row = 2;
        foreach (var cp in data.CutPoints)
        {
            ws.Cell(row, 1).Value = "裁切点";
            ws.Cell(row, 2).Value = cp;
            row++;
        }
        foreach (var seg in data.Segments)
        {
            ws.Cell(row, 1).Value = "片段";
            ws.Cell(row, 2).Value = seg.Start;
            ws.Cell(row, 3).Value = seg.End;
            ws.Cell(row, 4).Value = seg.Marked;
            row++;
        }
        foreach (var shot in data.Screenshots)
        {
            ws.Cell(row, 1).Value = "截图";
            ws.Cell(row, 2).Value = shot.Position;
            ws.Cell(row, 4).Value = shot.Marked;
            ws.Cell(row, 5).Value = shot.File;
            row++;
        }
        ws.Columns().AdjustToContents(1, 40);
        wb.SaveAs(path);
    }

    /// <summary>
    /// 解析人工编辑过的 xlsx 工作簿（宽容：表头/注释/空行忽略、
    /// 多时间码格式、TRUE/1/✓/√/是 等标记写法、空「结束」列）。非法行忽略。
    /// </summary>
    public static NotesExportData Import(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet(1);

        var data = new NotesExportData();
        foreach (var row in ws.RowsUsed())
        {
            if (row.RowNumber() == 1) continue; // 表头
            var kind = row.Cell(1).GetString().Trim();
            if (kind.Length == 0 || kind.StartsWith('#')) continue;

            var startText = row.Cell(2).GetString();
            var endText = row.Cell(3).GetString();
            switch (kind)
            {
                case "裁切点" when NotesDataCsvService.TryParseTime(startText, out var cp):
                    data.CutPoints.Add(NotesDataCsvService.FormatTime(cp));
                    break;
                case "片段" when NotesDataCsvService.TryParseTime(startText, out var start)
                              && NotesDataCsvService.TryParseTime(endText, out var end):
                    data.Segments.Add(new ExportSegment
                    {
                        Start = NotesDataCsvService.FormatTime(start),
                        End = NotesDataCsvService.FormatTime(end),
                        Marked = ReadMark(row.Cell(4)),
                    });
                    break;
                case "截图" when NotesDataCsvService.TryParseTime(startText, out var pos):
                    data.Screenshots.Add(new ExportScreenshot
                    {
                        Position = NotesDataCsvService.FormatTime(pos),
                        Marked = ReadMark(row.Cell(4)),
                        File = row.Cell(5).GetString().Trim(),
                    });
                    break;
            }
        }
        // 裁切点升序去重（与 CSV 导入一致）
        data.CutPoints = data.CutPoints
            .Select(t => NotesDataCsvService.TryParseTime(t, out var ts) ? ts : TimeSpan.Zero)
            .Where(t => t > TimeSpan.Zero)
            .Distinct()
            .OrderBy(t => t)
            .Select(NotesDataCsvService.FormatTime)
            .ToList();
        return data;
    }

    /// <summary>标记单元格读取：布尔原生值优先，文本走宽容解析。</summary>
    private static bool ReadMark(IXLCell cell) =>
        cell.DataType == XLDataType.Boolean
            ? cell.GetBoolean()
            : NotesDataCsvService.TryParseMark(cell.GetString());
}
