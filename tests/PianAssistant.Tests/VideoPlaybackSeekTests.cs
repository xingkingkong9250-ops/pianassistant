using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>seek 回弹抑制判定（v1.2.2）：IsStaleSeekReport 纯函数。</summary>
public class VideoPlaybackSeekTests
{
    [Fact]
    public void StaleReport_InWindow_FarFromTarget_IsSuppressed()
    {
        // seek 目标 60s，400ms 窗内收到 50s 旧位置帧 → 丢弃
        Assert.True(VideoPlaybackService.IsStaleSeekReport(
            50_000, 60_000, elapsedMs: 100));
    }

    [Fact]
    public void Report_NearTarget_IsAccepted()
    {
        // 偏差 ≤ 600ms 容差视为到位
        Assert.False(VideoPlaybackService.IsStaleSeekReport(
            60_400, 60_000, elapsedMs: 100));
    }

    [Fact]
    public void StaleReport_AfterWindow_IsAccepted()
    {
        // 超 400ms 抑制窗后恢复正常上报（兜底防卡死）
        Assert.False(VideoPlaybackService.IsStaleSeekReport(
            50_000, 60_000, elapsedMs: 450));
    }

    [Fact]
    public void Report_WithoutPendingSeek_IsAccepted()
    {
        // 无 pending seek（-1）时不抑制
        Assert.False(VideoPlaybackService.IsStaleSeekReport(
            50_000, -1, elapsedMs: 100));
    }

    [Fact]
    public void CustomWindowAndTolerance_AreHonored()
    {
        Assert.True(VideoPlaybackService.IsStaleSeekReport(
            60_700, 60_000, elapsedMs: 100, windowMs: 400, toleranceMs: 500));
        Assert.False(VideoPlaybackService.IsStaleSeekReport(
            60_700, 60_000, elapsedMs: 100, windowMs: 400, toleranceMs: 1_000));
    }
}
