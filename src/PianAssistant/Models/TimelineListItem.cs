using PianAssistant.Helpers;

namespace PianAssistant.Models;

/// <summary>统一时间线条目类型。</summary>
public enum TimelineItemKind
{
    Screenshot,
    Segment,
    CutPoint,
}

/// <summary>
/// 笔记面板统一时间线列表条目基类（W1-C2-D-03）：
/// 截图/片段/裁切点混排，按 Position 升序、同刻按
/// <see cref="KindSortOrder"/>（裁切点 → 片段 → 截图）排序。
/// </summary>
public abstract class TimelineListItem : ObservableObject
{
    public TimelineItemKind Kind { get; }
    /// <summary>同刻排序权重：裁切点 0 → 片段 1 → 截图 2。</summary>
    public int KindSortOrder { get; }
    public TimeSpan Position { get; }
    public string Title { get; }
    public string TimeText { get; }
    public string Icon { get; }
    /// <summary>条目主题色（截图绿/片段黄/裁切点红）。</summary>
    public string AccentColor { get; }
    /// <summary>是否带缩略图（裁切点为红色细条行，无缩略图）。</summary>
    public bool HasThumbnail { get; }

    private bool _isSelected;
    /// <summary>列表选中态（TwoWay 绑定 ListBox）。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private protected TimelineListItem(
        TimelineItemKind kind, int kindSortOrder, TimeSpan position,
        string title, string timeText, string icon, string accentColor, bool hasThumbnail)
    {
        Kind = kind;
        KindSortOrder = kindSortOrder;
        Position = position;
        Title = title;
        TimeText = timeText;
        Icon = icon;
        AccentColor = accentColor;
        HasThumbnail = hasThumbnail;
    }

    /// <summary>位置格式化（mm:ss.fff）。</summary>
    protected static string FormatTime(TimeSpan ts) => ts.ToString(@"mm\:ss\.fff");
}

/// <summary>截图条目：绿框，名称「截图 N」（按时间顺序编号）。</summary>
public sealed class ScreenshotListItem : TimelineListItem
{
    public ScreenshotEntry Entry { get; }

    public ScreenshotListItem(ScreenshotEntry entry, int number)
        : base(TimelineItemKind.Screenshot, 2, entry.Position,
            $"截图 {number}", FormatTime(entry.Position), "📷", "#4EC9B0", true)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
    }

    /// <summary>缩略图直接用截图文件本身。</summary>
    public string ThumbnailPath => Entry.OutputPath;
}

/// <summary>片段条目：黄框，名称「片段 N · 时长」，缩略图异步补抽。</summary>
public sealed class SegmentListItem : TimelineListItem
{
    public TimelineSegment Segment { get; }

    private string? _thumbnailPath;
    /// <summary>段起始帧缩略图路径（clips/thumbs/{startMs}.jpg）；未抽出前为 null。</summary>
    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        set => SetProperty(ref _thumbnailPath, value);
    }

    public SegmentListItem(TimelineSegment segment)
        : base(TimelineItemKind.Segment, 1, segment.Start,
            $"片段 {segment.Index} · {segment.DurationText}",
            FormatTime(segment.Start), "🎬", "#D7BA7D", true)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Segment = segment;
    }
}

/// <summary>裁切点条目：红色细条行（无缩略图），可选中删除以合并相邻片段。</summary>
public sealed class CutPointListItem : TimelineListItem
{
    public CutPointListItem(TimeSpan position)
        : base(TimelineItemKind.CutPoint, 0, position,
            $"裁切点 {FormatTime(position)}", FormatTime(position), "✂", "#E51400", false)
    {
    }
}
