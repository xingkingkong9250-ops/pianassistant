using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PianAssistant.Services;

/// <summary>
/// SRT 时间轴裁剪（v1.15.6）。
/// 背景：ffmpeg 对字幕输入做 <c>-ss</c> 时不会按片段 in-point 平移时间码
/// （实测内嵌/外挂字幕都会保留源时间轴，或按首条 cue 归零），
/// 于是片段字幕要么错位、要么整条落在片段之外而「看不到」。
/// 做法：整条字幕先导出为 SRT（快、无损），再由这里按片段起止精确平移/裁剪，
/// 最后合并进片段——保证字幕与画面严格对齐。
/// </summary>
public static class SubtitleTimeline
{
    private static readonly Regex RangeLine = new(
        @"^\s*(\d{2}):(\d{2}):(\d{2}),(\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2}),(\d{3})",
        RegexOptions.Compiled);

    /// <summary>
    /// 把 <paramref name="sourceSrt"/> 的每条字幕平移 -<paramref name="inPoint"/>，
    /// 裁掉片段外部分并限制在片段长度内，写入 <paramref name="targetSrt"/>。
    /// 返回写入的条数（0 表示片段内没有可用字幕）。
    /// </summary>
    public static int Shift(
        string sourceSrt, string targetSrt, TimeSpan inPoint, TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSrt);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSrt);
        if (duration <= TimeSpan.Zero) return 0;

        var lines = File.ReadAllLines(sourceSrt);
        var sb = new StringBuilder();
        var written = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            if (!TryParseRange(lines[i], out var start, out var end)) continue;

            // 收集本条字幕的文本行（到空行或下一条时间行为止）
            var text = new List<string>();
            var j = i + 1;
            while (j < lines.Length && !string.IsNullOrWhiteSpace(lines[j])
                   && !TryParseRange(lines[j], out _, out _))
            {
                text.Add(lines[j]);
                j++;
            }
            i = j;

            var newStart = start - inPoint;
            var newEnd = end - inPoint;
            if (newEnd <= TimeSpan.Zero || newStart >= duration) continue;
            if (newStart < TimeSpan.Zero) newStart = TimeSpan.Zero;
            if (newEnd > duration) newEnd = duration;
            if (newEnd <= newStart) continue;

            written++;
            sb.AppendLine(written.ToString(CultureInfo.InvariantCulture));
            sb.Append(FormatTime(newStart)).Append(" --> ").AppendLine(FormatTime(newEnd));
            foreach (var line in text) sb.AppendLine(line);
            sb.AppendLine();
        }

        File.WriteAllText(targetSrt, sb.ToString(), new UTF8Encoding(false));
        return written;
    }

    private static bool TryParseRange(string line, out TimeSpan start, out TimeSpan end)
    {
        start = end = TimeSpan.Zero;
        var m = RangeLine.Match(line);
        if (!m.Success) return false;
        start = new TimeSpan(0, ToInt(m.Groups[1]), ToInt(m.Groups[2]),
            ToInt(m.Groups[3]), ToInt(m.Groups[4]));
        end = new TimeSpan(0, ToInt(m.Groups[5]), ToInt(m.Groups[6]),
            ToInt(m.Groups[7]), ToInt(m.Groups[8]));
        return true;
    }

    private static int ToInt(Group g) =>
        int.Parse(g.Value, CultureInfo.InvariantCulture);

    /// <summary>SRT 时间码格式：HH:MM:SS,mmm。</summary>
    private static string FormatTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00},{3:000}",
            (int)t.TotalHours, t.Minutes, t.Seconds, t.Milliseconds);
    }
}
