using System.Globalization;
using System.IO;
using System.Text;
using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// 裁切点导入/导出：把时间轴裁切点写成文本清单（可再导入），
/// 并在相邻裁切点之间自动生成片段行（<c>起-止 片段 #N</c>），
/// 使导出文件同时可作为批量片段导出的清单。
/// </summary>
public static class ClipExportService
{
    /// <summary>导出文件版本头。</summary>
    public const string HeaderV1 = "# PianAssistant 裁切点导出 v1";

    /// <summary>
    /// 导出裁切点清单：每行一个裁切点（HH:MM:SS），相邻裁切点（含 00:00:00 起点
    /// 与视频结尾 length）之间生成「HH:MM:SS-HH:MM:SS 片段 #N」行。
    /// </summary>
    public static void ExportCutPoints(string path, IReadOnlyList<TimeSpan> cutPoints, TimeSpan length)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(cutPoints);

        // 升序去重（导入端同样按序处理）
        var points = cutPoints.Where(p => p > TimeSpan.Zero).OrderBy(p => p).Distinct().ToList();

        var sb = new StringBuilder();
        sb.AppendLine(HeaderV1);
        sb.AppendLine("# 每行一个裁切点：HH:MM:SS；片段段：HH:MM:SS-HH:MM:SS 片段 标签");

        if (points.Count == 0)
        {
            // 无裁切点：仅整段一个片段行（length 未知时不写）
            if (length > TimeSpan.Zero)
                sb.AppendLine($"{FormatTime(TimeSpan.Zero)}-{FormatTime(length)} 片段 #1");
        }
        else
        {
            var previous = TimeSpan.Zero;
            for (int i = 0; i < points.Count; i++)
            {
                // 片段行在前、裁切点行在后：0→cp1, cp1, cp1→cp2, cp2, …
                sb.AppendLine($"{FormatTime(previous)}-{FormatTime(points[i])} 片段 #{i + 1}");
                sb.AppendLine(FormatTime(points[i]));
                previous = points[i];
            }
            // 最后一个片段：末裁切点 → 视频结尾（length 未知或等于末点时省略）
            if (length > points[^1])
                sb.AppendLine($"{FormatTime(previous)}-{FormatTime(length)} 片段 #{points.Count + 1}");
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>
    /// 导入裁切点清单：复用 <see cref="BatchTextParser"/> 解析 CutPoint/Clip 行
    /// （注释与无效行自动忽略）。返回升序去重后的裁切点与片段段列表；
    /// 片段行仅在 <paramref name="sourcePath"/> 非空时构造 <see cref="ClipRange"/>
    /// （无源视频无法构造范围）。
    /// </summary>
    public static (IReadOnlyList<TimeSpan> CutPoints, IReadOnlyList<ClipRange> Segments) ImportCutPoints(
        string path, string? sourcePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var text = File.ReadAllText(path);
        var instructions = new BatchTextParser().Parse(text);

        var cutPoints = new List<TimeSpan>();
        var segments = new List<ClipRange>();
        foreach (var ins in instructions)
        {
            if (!ins.IsValid) continue;
            switch (ins.Type)
            {
                case BatchActionKind.CutPoint:
                    cutPoints.Add(ins.Start);
                    break;
                case BatchActionKind.Clip when ins.End is { } end:
                    if (!string.IsNullOrWhiteSpace(sourcePath))
                        segments.Add(new ClipRange(sourcePath, ins.Start, end, ins.Label));
                    break;
            }
        }
        return (cutPoints.Distinct().OrderBy(p => p).ToList(), segments);
    }

    /// <summary>时间码格式化：HH:MM:SS（毫秒非零时带 .mmm）。</summary>
    private static string FormatTime(TimeSpan t)
    {
        t = t < TimeSpan.Zero ? TimeSpan.Zero : t;
        var baseText = string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2}",
            (int)t.TotalHours, t.Minutes, t.Seconds);
        return t.Milliseconds > 0 ? $"{baseText}.{t.Milliseconds:D3}" : baseText;
    }
}
