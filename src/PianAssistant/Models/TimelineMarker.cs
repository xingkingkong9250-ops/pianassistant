namespace PianAssistant.Models;

/// <summary>时间轴标记类型。</summary>
public enum MarkerKind
{
    Note,        // 笔记 — 蓝色
    Screenshot,  // 截图 — 青色
    Clip,        // 片段 — 橙色
}

/// <summary>
/// 时间轴标记点：由活动流条目同步生成，供 TimelineControl 在
/// 播放头三角下方一行绘制小色块。
/// 模型层不用 UI 类型，颜色以十六进制字符串表示。
/// </summary>
public sealed class TimelineMarker
{
    public TimeSpan Position { get; }
    public MarkerKind Kind { get; }

    /// <summary>标记颜色（十六进制，如 "#569CD6"）。</summary>
    public string ColorHex { get; }

    public TimelineMarker(TimeSpan position, MarkerKind kind, string? colorHex = null)
    {
        Position = position;
        Kind = kind;
        ColorHex = colorHex ?? Kind switch
        {
            MarkerKind.Note => "#569CD6",        // 笔记 — 蓝
            MarkerKind.Screenshot => "#4EC9B0",  // 截图 — 青
            MarkerKind.Clip => "#CE9178",        // 片段 — 橙
            _ => "#808080",
        };
    }
}
