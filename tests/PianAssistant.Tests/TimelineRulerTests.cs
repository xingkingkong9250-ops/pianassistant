using PianAssistant.Views.Controls;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>时间轴刻度自适应纯函数测试（v1.13.0）：间隔阶梯选择与标签格式化。</summary>
public class TimelineRulerTests
{
    [Theory]
    [InlineData(0.1, 1)]        // 需要 ≥0.1s → 最小阶梯 1s
    [InlineData(1.0, 1)]
    [InlineData(1.5, 2)]
    [InlineData(4.9, 5)]
    [InlineData(31.0, 60)]      // 超过 30s → 1 分钟
    [InlineData(61.0, 120)]
    [InlineData(300.0, 300)]
    [InlineData(1900.0, 3600)]  // 超过 30 分钟 → 1 小时
    [InlineData(3600.0, 3600)]
    public void PickRulerInterval_ChoosesSmallestStepNotBelowMin(double minSeconds, double expected)
    {
        Assert.Equal(expected, TimelineControl.PickRulerInterval(minSeconds));
    }

    [Fact]
    public void PickRulerInterval_ExtremeZoomOut_ReturnsMaxStep()
    {
        Assert.Equal(43200, TimelineControl.PickRulerInterval(1e6));
    }

    [Theory]
    [InlineData(0, 60, "00:00")]
    [InlineData(90, 60, "01:30")]           // 短轴用 mm:ss
    [InlineData(3600, 3600, "1:00:00")]     // 间隔≥1小时 → h:mm:ss
    [InlineData(3661, 3600, "1:01:01")]
    [InlineData(5400, 300, "1:30:00")]      // 间隔<1小时但时间本身≥1小时 → h:mm:ss
    public void FormatRulerLabel_PicksUnitByStepAndValue(double seconds, double step, string expected)
    {
        Assert.Equal(expected, TimelineControl.FormatRulerLabel(TimeSpan.FromSeconds(seconds), step));
    }
}
