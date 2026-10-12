using System.Globalization;
using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// 批量文本解析器：把多行文本解析为批量指令列表。
/// 行格式（<c>#</c> 或 <c>//</c> 开头为注释，空行忽略）：
///   <c>HH:MM:SS 截图 标签</c>          → 截图（别名 screenshot；标签可选）
///   <c>HH:MM:SS-HH:MM:SS 片段 标签</c> → 片段（别名 clip；也接受仅时间段）
///   <c>HH:MM:SS 标记 标签</c>          → 标记/笔记（别名 笔记/note；标签必填）
///   <c>HH:MM:SS</c>                    → 裁切点
/// 时间支持 HH:MM:SS(.mmm) 与 MM:SS(.mmm)；全角冒号「：」容错；允许乱序。
/// 解析失败的行不抛异常，标记 IsValid=false 并在 Error 中说明行号与原因。
/// </summary>
public sealed class BatchTextParser
{
    /// <summary>解析文本为指令列表（按行，LineNumber 从 1 开始）。</summary>
    public IReadOnlyList<BatchInstruction> Parse(string? text)
    {
        var list = new List<BatchInstruction>();
        if (string.IsNullOrWhiteSpace(text)) return list;

        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].Trim();
            var lineNumber = i + 1;
            if (raw.Length == 0) continue;
            // 注释行：# 或 // 开头（导出文件头与用户注释均跳过）
            if (raw.StartsWith('#') || raw.StartsWith("//", StringComparison.Ordinal)) continue;
            list.Add(ParseLine(raw, lineNumber));
        }
        return list;
    }

    private static BatchInstruction ParseLine(string raw, int lineNumber)
    {
        // 容错：全角冒号/全角空格 → 半角；em/en 破折号 → 普通连字符
        var line = raw.Replace('：', ':').Replace('　', ' ')
            .Replace('—', '-').Replace('–', '-');

        var spaceIdx = line.IndexOf(' ');
        var head = spaceIdx < 0 ? line : line[..spaceIdx];
        var rest = spaceIdx < 0 ? string.Empty : line[(spaceIdx + 1)..].Trim();

        // 时间段：头 token 含 '-' → 片段行
        var dashIdx = head.IndexOf('-');
        if (dashIdx > 0)
            return ParseClipLine(head, dashIdx, rest, lineNumber);

        // 单时间
        if (!TryParseTime(head, out var start))
            return BatchInstruction.Invalid(lineNumber, $"第 {lineNumber} 行：无法识别的时间「{head}」");

        // 仅时间 → 裁切点
        if (rest.Length == 0)
            return BatchInstruction.Valid(BatchActionKind.CutPoint, start, null, null, lineNumber);

        var kwEnd = rest.IndexOf(' ');
        var keyword = kwEnd < 0 ? rest : rest[..kwEnd];
        var label = kwEnd < 0 ? null : rest[(kwEnd + 1)..].Trim();
        if (string.IsNullOrEmpty(label)) label = null;

        if (IsScreenshotKeyword(keyword))
            return BatchInstruction.Valid(BatchActionKind.Screenshot, start, null, label, lineNumber);
        if (IsMarkerKeyword(keyword))
        {
            return label is null
                ? BatchInstruction.Invalid(lineNumber, $"第 {lineNumber} 行：标记指令需要标签文本")
                : BatchInstruction.Valid(BatchActionKind.Marker, start, null, label, lineNumber);
        }
        if (IsCutPointKeyword(keyword))
            return BatchInstruction.Valid(BatchActionKind.CutPoint, start, null, null, lineNumber);
        if (IsClipKeyword(keyword))
            return BatchInstruction.Invalid(lineNumber, $"第 {lineNumber} 行：片段指令需要时间段（HH:MM:SS-HH:MM:SS）");

        return BatchInstruction.Invalid(lineNumber, $"第 {lineNumber} 行：无法识别的指令关键词「{keyword}」");
    }

    /// <summary>解析时间段行：<c>起-止 [片段/clip] [标签]</c>。</summary>
    private static BatchInstruction ParseClipLine(string head, int dashIdx, string rest, int lineNumber)
    {
        var startText = head[..dashIdx];
        var endText = head[(dashIdx + 1)..];
        if (!TryParseTime(startText, out var start))
            return BatchInstruction.Invalid(lineNumber, $"第 {lineNumber} 行：无法识别的起始时间「{startText}」");
        if (!TryParseTime(endText, out var end))
            return BatchInstruction.Invalid(lineNumber, $"第 {lineNumber} 行：无法识别的结束时间「{endText}」");
        if (end <= start)
            return BatchInstruction.Invalid(lineNumber, $"第 {lineNumber} 行：结束时间必须晚于起始时间");

        // 关键字可省略；存在时必须是 片段/clip
        string? label = null;
        if (rest.Length > 0)
        {
            var kwEnd = rest.IndexOf(' ');
            var keyword = kwEnd < 0 ? rest : rest[..kwEnd];
            if (!IsClipKeyword(keyword))
                return BatchInstruction.Invalid(lineNumber,
                    $"第 {lineNumber} 行：时间段后仅支持「片段/clip」指令，无法识别「{keyword}」");
            label = kwEnd < 0 ? null : rest[(kwEnd + 1)..].Trim();
            if (string.IsNullOrEmpty(label)) label = null;
        }
        return BatchInstruction.Valid(BatchActionKind.Clip, start, end, label, lineNumber);
    }

    // --- 关键词判定（英文别名不区分大小写） ---

    private static bool IsScreenshotKeyword(string keyword) => keyword.Equals("截图", StringComparison.Ordinal)
        || keyword.Equals("screenshot", StringComparison.OrdinalIgnoreCase);

    private static bool IsClipKeyword(string keyword) => keyword.Equals("片段", StringComparison.Ordinal)
        || keyword.Equals("clip", StringComparison.OrdinalIgnoreCase);

    private static bool IsMarkerKeyword(string keyword) =>
        keyword is "标记" or "笔记"
        || keyword.Equals("note", StringComparison.OrdinalIgnoreCase);

    private static bool IsCutPointKeyword(string keyword) =>
        keyword is "裁切点" or "裁切"
        || keyword.Equals("cutpoint", StringComparison.OrdinalIgnoreCase)
        || keyword.Equals("cut", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 解析时间 token：支持 HH:MM:SS(.mmm)、MM:SS(.mmm) 与纯秒数。
    /// 不直接用 TimeSpan.TryParse：它会把「MM:SS」解析为「HH:MM」。
    /// </summary>
    private static bool TryParseTime(string token, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        var parts = token.Split(':');
        if (parts.Length is < 1 or > 3) return false;

        // 末段可带毫秒小数（.mmm）
        double fraction = 0;
        var last = parts[^1];
        var dotIdx = last.IndexOf('.');
        if (dotIdx >= 0)
        {
            var fracText = last[(dotIdx + 1)..];
            if (fracText.Length == 0 || !fracText.All(char.IsDigit)) return false;
            fraction = double.Parse("0." + fracText, CultureInfo.InvariantCulture);
            last = last[..dotIdx];
        }
        if (last.Length == 0 || !last.All(char.IsDigit)) return false;
        if (!int.TryParse(last, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return false;

        // 多段时秒位必须 0..59；单段视为纯秒数
        if (parts.Length > 1 && seconds > 59) return false;

        var minutes = 0;
        var hours = 0;
        if (parts.Length >= 2)
        {
            var mm = parts[^2];
            if (mm.Length == 0 || !mm.All(char.IsDigit)) return false;
            minutes = int.Parse(mm, CultureInfo.InvariantCulture);
        }
        if (parts.Length == 3)
        {
            var hh = parts[0];
            if (hh.Length == 0 || !hh.All(char.IsDigit)) return false;
            hours = int.Parse(hh, CultureInfo.InvariantCulture);
        }

        value = TimeSpan.FromHours(hours)
            + TimeSpan.FromMinutes(minutes)
            + TimeSpan.FromSeconds(seconds + fraction);
        return value >= TimeSpan.Zero;
    }
}
