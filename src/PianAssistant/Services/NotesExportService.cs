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
}
