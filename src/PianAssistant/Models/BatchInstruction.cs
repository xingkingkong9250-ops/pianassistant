namespace PianAssistant.Models;

/// <summary>批量指令类型。</summary>
public enum BatchActionKind
{
    /// <summary>截图（ffmpeg 单帧）。</summary>
    Screenshot,
    /// <summary>片段（时间段截取）。</summary>
    Clip,
    /// <summary>标记/笔记（文本层直接写入活动流）。</summary>
    Marker,
    /// <summary>裁切点（文本层直接写入播放器集合）。</summary>
    CutPoint,
}

/// <summary>
/// 一条批量指令（由 <see cref="Services.BatchTextParser"/> 从文本行解析而来）。
/// 解析失败的行不抛异常：IsValid=false 且 Error 说明行号与原因。
/// </summary>
public sealed class BatchInstruction
{
    public BatchActionKind Type { get; private init; }
    public TimeSpan Start { get; private init; }
    public TimeSpan? End { get; private init; }
    public string? Label { get; private init; }
    public int LineNumber { get; private init; }
    public bool IsValid { get; private init; }
    public string? Error { get; private init; }

    /// <summary>构造一条解析成功的指令。</summary>
    public static BatchInstruction Valid(BatchActionKind type, TimeSpan start,
        TimeSpan? end, string? label, int lineNumber) => new()
    {
        Type = type,
        Start = start,
        End = end,
        Label = label,
        LineNumber = lineNumber,
        IsValid = true,
    };

    /// <summary>构造一条解析失败的指令（Error 说明行号与原因）。</summary>
    public static BatchInstruction Invalid(int lineNumber, string error) => new()
    {
        LineNumber = lineNumber,
        IsValid = false,
        Error = error,
    };

    // --- 以下为 UI 绑定辅助属性（纯字符串，不依赖 UI 类型） ---

    /// <summary>类型显示文本。</summary>
    public string TypeText => Type switch
    {
        BatchActionKind.Screenshot => "截图",
        BatchActionKind.Clip => "片段",
        BatchActionKind.Marker => "标记",
        BatchActionKind.CutPoint => "裁切点",
        _ => "未知",
    };

    /// <summary>时间显示文本（片段为「起-止」，其余为单时间）。</summary>
    public string TimeText => End is { } end
        ? $"{FormatTime(Start)}-{FormatTime(end)}"
        : FormatTime(Start);

    /// <summary>解析状态图标（✔ 有效 / ✘ 错误）。</summary>
    public string StatusIcon => IsValid ? "✔" : "✘";

    private static string FormatTime(TimeSpan t)
    {
        t = t < TimeSpan.Zero ? TimeSpan.Zero : t;
        var baseText = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0:D2}:{1:D2}:{2:D2}", (int)t.TotalHours, t.Minutes, t.Seconds);
        return t.Milliseconds > 0 ? $"{baseText}.{t.Milliseconds:D3}" : baseText;
    }
}
