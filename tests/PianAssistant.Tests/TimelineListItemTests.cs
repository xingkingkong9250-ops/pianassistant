using PianAssistant.Models;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// 统一时间线条目模型（W1-C2-D-03）：截图/片段/裁切点三类，
/// 排序键 = Position 升序，同刻次序 裁切点 → 片段 → 截图。
/// </summary>
public class TimelineListItemTests
{
    private static ScreenshotEntry Shot(double sec) =>
        new("video.mp4", $"shot_{sec}.png", TimeSpan.FromSeconds(sec), new DateTime(2026, 10, 17));

    [Fact]
    public void KindSortOrder_CutPointBeforeSegmentBeforeScreenshot()
    {
        Assert.True(new CutPointListItem(TimeSpan.Zero).KindSortOrder
            < new SegmentListItem(new TimelineSegment(1, TimeSpan.Zero, TimeSpan.FromSeconds(1))).KindSortOrder);
        Assert.True(new SegmentListItem(new TimelineSegment(1, TimeSpan.Zero, TimeSpan.FromSeconds(1))).KindSortOrder
            < new ScreenshotListItem(Shot(0), 1).KindSortOrder);
    }

    [Fact]
    public void ScreenshotItem_TitleIsNumberedByTimeOrder()
    {
        var item = new ScreenshotListItem(Shot(5), 3);
        Assert.Equal(TimelineItemKind.Screenshot, item.Kind);
        Assert.Equal("截图 3", item.Title);
        Assert.Equal("📷", item.Icon);
        Assert.Equal("00:05.000", item.TimeText);
        Assert.Equal("shot_5.png", item.ThumbnailPath);
        Assert.True(item.HasThumbnail);
        Assert.Equal("#4EC9B0", item.AccentColor); // 绿框
    }

    [Fact]
    public void SegmentItem_TitleContainsIndexAndDuration()
    {
        var seg = new TimelineSegment(2, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(25.5));
        var item = new SegmentListItem(seg);
        Assert.Equal(TimelineItemKind.Segment, item.Kind);
        Assert.Equal("片段 2 · 00:15.500", item.Title);
        Assert.Equal("🎬", item.Icon);
        Assert.Equal("00:10.000", item.TimeText);
        Assert.True(item.HasThumbnail);
        Assert.Null(item.ThumbnailPath); // 异步补抽前为空
        Assert.Equal("#D7BA7D", item.AccentColor); // 黄框
    }

    [Fact]
    public void SegmentItem_ThumbnailPath_NotifiesOnSet()
    {
        var item = new SegmentListItem(new TimelineSegment(1, TimeSpan.Zero, TimeSpan.FromSeconds(5)));
        var raised = 0;
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SegmentListItem.ThumbnailPath)) raised++; };
        item.ThumbnailPath = @"thumbs\0.jpg";
        Assert.Equal(@"thumbs\0.jpg", item.ThumbnailPath);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void CutPointItem_RedBarWithoutThumbnail()
    {
        var item = new CutPointListItem(TimeSpan.FromSeconds(90));
        Assert.Equal(TimelineItemKind.CutPoint, item.Kind);
        Assert.Equal("裁切点 01:30.000", item.Title);
        Assert.Equal("✂", item.Icon);
        Assert.False(item.HasThumbnail);
        Assert.Equal("#E51400", item.AccentColor); // 红色标记
    }

    [Fact]
    public void IsSelected_TwoWay_Notifies()
    {
        var item = new CutPointListItem(TimeSpan.Zero);
        var raised = 0;
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TimelineListItem.IsSelected)) raised++; };
        item.IsSelected = true;
        Assert.True(item.IsSelected);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void IsMarked_TwoWay_Notifies_IndependentFromSelection()
    {
        var item = new CutPointListItem(TimeSpan.Zero);
        var raised = 0;
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TimelineListItem.IsMarked)) raised++; };
        item.IsMarked = true;
        Assert.True(item.IsMarked);
        Assert.False(item.IsSelected); // 标记与列表选中互不影响
        Assert.Equal(1, raised);
    }
}
