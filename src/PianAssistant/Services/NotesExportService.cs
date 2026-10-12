using System.Globalization;
using System.IO;
using System.Text;
using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// 活动流导出：把活动条目（笔记/截图/片段/裁切点）写成带时间码的
/// Markdown 或纯文本文件。纯 IO，不引用任何 UI 类型。
/// </summary>
public static class NotesExportService
{
    /// <summary>按扩展名导出：.md → Markdown，其余 → 纯文本。</summary>
    public static void Export(IEnumerable<ActivityEntry> activities, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (ext == "md" || ext == "markdown")
            ExportMarkdown(activities, path);
        else
            ExportText(activities, path);
    }

    /// <summary>
    /// 导出 Markdown：笔记以 `## 00:01:23.500 — 笔记` 标题呈现，
    /// 截图/片段/裁切点作为列表项（带输出路径）。
    /// </summary>
    public static void ExportMarkdown(IEnumerable<ActivityEntry> activities, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var sb = new StringBuilder();
        sb.AppendLine("# 拉片笔记");
        sb.AppendLine();
        sb.AppendLine($"> 导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        foreach (var a in activities)
        {
            if (a.Type == ActivityType.Note)
            {
                sb.AppendLine($"## {a.PositionText} — 笔记");
                sb.AppendLine(a.Label);
                sb.AppendLine();
            }
            else
            {
                var line = $"- {a.TypeIcon} {a.Label}";
                if (!string.IsNullOrEmpty(a.FilePath))
                    line += $" — `{a.FilePath}`";
                sb.AppendLine(line);
            }
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>导出纯文本：`[00:01:23.500] 笔记：xxx`，其余条目缩进列表。</summary>
    public static void ExportText(IEnumerable<ActivityEntry> activities, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var sb = new StringBuilder();
        sb.AppendLine("拉片笔记");
        sb.AppendLine($"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(new string('=', 32));

        foreach (var a in activities)
        {
            if (a.Type == ActivityType.Note)
            {
                sb.AppendLine();
                sb.AppendLine($"[{a.PositionText}] 笔记：{a.Label}");
            }
            else
            {
                var line = $"  - {a.TypeIcon} {a.Label}";
                if (!string.IsNullOrEmpty(a.FilePath))
                    line += $"（{a.FilePath}）";
                sb.AppendLine(line);
            }
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>
    /// 整理导出（M5）：结构化 Markdown 报告 —— 头部统计、按位置排序的时间线活动、
    /// 分段汇总。活动条目按位置升序重排；片段条目按出现顺序编号 #N 并标注时长。
    /// </summary>
    public static void ExportActivityReport(
        IEnumerable<ActivityEntry> activities,
        IReadOnlyList<TimelineSegment> segments,
        string path,
        string videoTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(segments);
        var list = (activities ?? Array.Empty<ActivityEntry>()).OrderBy(a => a.Position).ToList();

        var notes = list.Count(a => a.Type == ActivityType.Note);
        var screenshots = list.Count(a => a.Type == ActivityType.Screenshot);
        var clips = list.Count(a => a.Type == ActivityType.Clip);
        var cutPoints = list.Count(a => a.Type == ActivityType.CutPoint);

        var sb = new StringBuilder();
        sb.AppendLine($"# 拉片笔记 — {videoTitle}");
        sb.AppendLine();
        sb.AppendLine($"> 导出时间：{DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"> 共 笔记{notes} 截图{screenshots} 片段{clips} 裁切点{cutPoints}");
        sb.AppendLine();
        sb.AppendLine("## 时间线活动（按位置排序）");
        sb.AppendLine();

        var clipIndex = 0;
        foreach (var a in list)
        {
            switch (a.Type)
            {
                case ActivityType.Note:
                    sb.AppendLine($"[{FormatTime(a.Position)}] 📝 笔记：{a.Label}");
                    break;
                case ActivityType.CutPoint:
                    sb.AppendLine($"[{FormatTime(a.Position)}] 🔴 裁切点");
                    break;
                case ActivityType.Screenshot:
                    sb.AppendLine($"[{FormatTime(a.Position)}] 📷 截图{AppendPath(a.FilePath)}");
                    break;
                case ActivityType.Clip:
                    clipIndex++;
                    var range = a.Duration is { } duration
                        ? $"[{FormatTime(a.Position)}-{FormatTime(a.Position + duration)}]"
                        : $"[{FormatTime(a.Position)}]";
                    var durationText = a.Duration is { } d ? $"（{FormatDuration(d)}）" : string.Empty;
                    sb.AppendLine($"{range} ✂ 片段 #{clipIndex}{durationText}{AppendPath(a.FilePath)}");
                    break;
            }
        }

        if (segments.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"## 分段汇总（共 {segments.Count} 段）");
            sb.AppendLine();
            foreach (var s in segments)
                sb.AppendLine($"- #{s.Index} {FormatTime(s.Start)} → {FormatTime(s.End)}（{FormatDuration(s.Duration)}）");
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>输出路径后缀：有路径时返回「：路径」，否则空串。</summary>
    private static string AppendPath(string? filePath) =>
        string.IsNullOrEmpty(filePath) ? string.Empty : $"：{filePath}";

    /// <summary>时间码：HH:MM:SS（毫秒非零时带 .mmm）。</summary>
    private static string FormatTime(TimeSpan t)
    {
        t = t < TimeSpan.Zero ? TimeSpan.Zero : t;
        var baseText = string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}:{2:D2}",
            (int)t.TotalHours, t.Minutes, t.Seconds);
        return t.Milliseconds > 0 ? $"{baseText}.{t.Milliseconds:D3}" : baseText;
    }

    /// <summary>时长中文格式：X小时X分X秒 / X分X秒 / X秒（不足 1 秒显示毫秒）。</summary>
    private static string FormatDuration(TimeSpan d)
    {
        d = d < TimeSpan.Zero ? TimeSpan.Zero : d;
        var hours = (int)d.TotalHours;
        var minutes = d.Minutes;
        var seconds = d.Seconds;
        if (hours > 0) return $"{hours}小时{minutes}分{seconds}秒";
        if (minutes > 0) return $"{minutes}分{seconds}秒";
        if (seconds > 0 || d.Milliseconds == 0) return $"{seconds}秒";
        return $"{d.Milliseconds}毫秒";
    }
}
