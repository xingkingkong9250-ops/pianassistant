﻿using System.Globalization;
using System.IO;
using System.Text;
using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// 「导出数据」CSV 表格序列化（W1-C2-D-02）：把当前视频的裁切点、
/// 自动分段、截图与勾选标记写成人工可编辑的表格（Excel 直接打开），
/// 并可把人工修改后的表格解析回数据模型用于导入覆盖。
/// 列定义：<c>类型,开始,结束,标记,文件</c>（裁切点用「开始」列；文件列放最后，
/// 路径含逗号也安全）。UTF-8 带 BOM（Excel 兼容），时间码 HH:mm:ss.fff。
/// </summary>
public static class NotesDataCsvService
{
    /// <summary>CSV 表头。</summary>
    public const string Header = "类型,开始,结束,标记,文件";

    /// <summary>导出为 CSV 表格（UTF-8 带 BOM，Excel 直接打开不乱码）。</summary>
    public static void Export(string path, NotesExportData data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(data);

        var sb = new StringBuilder();
        sb.AppendLine(Header);
        foreach (var cp in data.CutPoints)
            sb.Append("裁切点,").Append(cp).AppendLine(",,,");
        foreach (var seg in data.Segments)
            sb.Append("片段,").Append(seg.Start).Append(',').Append(seg.End).Append(',')
              .Append(seg.Marked ? "✓," : ",").AppendLine();
        foreach (var shot in data.Screenshots)
            sb.Append("截图,").Append(shot.Position).Append(",,")
              .Append(shot.Marked ? "✓," : ",").AppendLine(shot.File);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    /// <summary>
    /// 解析人工编辑过的 CSV 表格。宽容处理：表头/注释/空行忽略、
    /// 时间码多格式（HH:mm:ss.fff / HH:mm:ss / mm:ss.fff / mm:ss）、
    /// 标记多写法（✓ √ true 1 是 y）。非法行忽略。
    /// </summary>
    public static NotesExportData Import(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var data = new NotesExportData();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("类型,"))
                continue;
            // 文件列最后：最多切 5 段，路径中的逗号保留在第 5 段
            var cols = line.Split(',', 5);
            if (cols.Length < 2) continue;
            var kind = cols[0].Trim();
            switch (kind)
            {
                case "裁切点" when TryParseTime(cols.Length > 1 ? cols[1] : null, out var cp):
                    data.CutPoints.Add(FormatTime(cp));
                    break;
                case "片段" when TryParseTime(cols.Length > 1 ? cols[1] : null, out var start)
                              && TryParseTime(cols.Length > 2 ? cols[2] : null, out var end):
                    data.Segments.Add(new ExportSegment
                    {
                        Start = FormatTime(start),
                        End = FormatTime(end),
                        Marked = cols.Length > 3 && TryParseMark(cols[3]),
                    });
                    break;
                case "截图" when TryParseTime(cols.Length > 1 ? cols[1] : null, out var pos):
                    data.Screenshots.Add(new ExportScreenshot
                    {
                        Position = FormatTime(pos),
                        Marked = cols.Length > 3 && TryParseMark(cols[3]),
                        File = cols.Length > 4 ? cols[4].Trim() : string.Empty,
                    });
                    break;
            }
        }
        // 裁切点升序去重
        data.CutPoints = data.CutPoints
            .Select(t => TryParseTime(t, out var ts) ? ts : TimeSpan.Zero)
            .Where(t => t > TimeSpan.Zero)
            .Distinct()
            .OrderBy(t => t)
            .Select(FormatTime)
            .ToList();
        return data;
    }

    /// <summary>时间码解析（宽容多格式）；解析失败返回 false。</summary>
    public static bool TryParseTime(string? text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim().Trim('"');
        string[] formats = ["hh\\:mm\\:ss\\.fff", "hh\\:mm\\:ss", "mm\\:ss\\.fff", "mm\\:ss"];
        if (TimeSpan.TryParseExact(text, formats, CultureInfo.InvariantCulture, out value)) return true;
        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>时间码统一格式化 HH:mm:ss.fff。</summary>
    public static string FormatTime(TimeSpan t) =>
        ((t < TimeSpan.Zero ? TimeSpan.Zero : t).ToString(@"hh\:mm\:ss\.fff"));

    /// <summary>标记解析：✓/√/x/true/1/是/y 视为已标记（xlsx 导入共用）。</summary>
    public static bool TryParseMark(string text)
    {
        var t = text.Trim().Trim('"').ToLowerInvariant();
        return t is "✓" or "√" or "x" or "true" or "1" or "是" or "y" or "yes";
    }
}
