using PianAssistant.Helpers;

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
/// v1.15.1：变为可观察对象，携带 IsSelected（笔记列表行选中）与
/// IsMarked（☑ 勾选用于导出）状态，供 TimelineControl 绘制选中高亮/金色标记态。
/// </summary>
public sealed class TimelineMarker : ObservableObject
{
    public TimeSpan Position { get; }
    public MarkerKind Kind { get; }

    /// <summary>标记颜色（十六进制，如 "#569CD6"）。</summary>
    public string ColorHex { get; }

    private bool _isSelected;
    /// <summary>笔记列表行选中态 — 选中时水滴加白色描边并略放大（v1.15.1）。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private bool _isMarked;
    /// <summary>笔记 ☑ 勾选标记态（用于导出） — 标记时水滴呈金色（与轴2黄带同色系，v1.15.1）。</summary>
    public bool IsMarked
    {
        get => _isMarked;
        set => SetProperty(ref _isMarked, value);
    }

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
