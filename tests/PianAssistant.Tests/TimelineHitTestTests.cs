using PianAssistant.Models;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>时间轴拖拽纯函数测试（v1.7.0）。</summary>
public class TimelineHitTestTests
{
    private static readonly TimeSpan[] Points =
    {
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(30),
    };

    [Fact]
    public void FindNearest_WithinTolerance_ReturnsNearestIndex()
    {
        // 距 10s 点 50ms，距 20s 点更远 → 命中下标 0
        var idx = TimelineHitTest.FindNearest(Points, TimeSpan.FromMilliseconds(10050), 100);
        Assert.Equal(0, idx);
    }

    [Fact]
    public void FindNearest_BeyondTolerance_ReturnsMinusOne()
    {
        var idx = TimelineHitTest.FindNearest(Points, TimeSpan.FromSeconds(15), 100);
        Assert.Equal(-1, idx);
    }

    [Fact]
    public void FindNearest_EmptyList_ReturnsMinusOne()
    {
        Assert.Equal(-1, TimelineHitTest.FindNearest(Array.Empty<TimeSpan>(), TimeSpan.Zero, 100));
    }

    [Fact]
    public void ClampCutPointMove_ClampsAtNeighbors()
    {
        // 移动 20s（下标 1）：越过 30s 邻居 → 钳制在 29.9s
        var r = TimelineHitTest.ClampCutPointMove(Points, 1, TimeSpan.FromSeconds(35), TimeSpan.FromMinutes(3));
        Assert.Equal(TimeSpan.FromSeconds(29.9), r);
        // 越过 10s 邻居 → 钳制在 10.1s
        r = TimelineHitTest.ClampCutPointMove(Points, 1, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(3));
        Assert.Equal(TimeSpan.FromSeconds(10.1), r);
    }

    [Fact]
    public void ClampCutPointMove_ClampsToEdges()
    {
        // 首点向左越界 → 0；尾点向右越界 → 时长
        var r = TimelineHitTest.ClampCutPointMove(Points, 0, TimeSpan.FromSeconds(-5), TimeSpan.FromMinutes(3));
        Assert.Equal(TimeSpan.Zero, r);
        r = TimelineHitTest.ClampCutPointMove(Points, 2, TimeSpan.FromHours(1), TimeSpan.FromMinutes(3));
        Assert.Equal(TimeSpan.FromMinutes(3), r);
    }

    [Fact]
    public void ClampCutPointMove_InsideRange_PassesThrough()
    {
        var r = TimelineHitTest.ClampCutPointMove(Points, 1, TimeSpan.FromSeconds(25), TimeSpan.FromMinutes(3));
        Assert.Equal(TimeSpan.FromSeconds(25), r);
    }
}
