using LibVLCSharp.Shared;
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

    // --- 空格播放/暂停状态判定（v1.7.2：倍速下 Buffering 被误判为未播放）---

    [Theory]
    [InlineData(VLCState.Playing)]
    [InlineData(VLCState.Buffering)]
    [InlineData(VLCState.Opening)]
    public void IsPlaybackActive_TrueForPlayingAndBuffering(VLCState state)
    {
        // 倍速播放频繁重缓冲：Buffering 必须视为「正在播放」，空格才能暂停而不是重复 Play
        Assert.True(VideoPlaybackService.IsPlaybackActive(state));
    }

    [Theory]
    [InlineData(VLCState.NothingSpecial)]
    [InlineData(VLCState.Paused)]
    [InlineData(VLCState.Stopped)]
    [InlineData(VLCState.Ended)]
    [InlineData(VLCState.Error)]
    public void IsPlaybackActive_FalseForInactiveStates(VLCState state)
    {
        Assert.False(VideoPlaybackService.IsPlaybackActive(state));
    }

    // --- 高速档音轨策略判定（v1.7.3：原生调用下沉后台控制线程后仍需正确触发）---

    [Theory]
    [InlineData(6.0)]
    [InlineData(8.0)]
    [InlineData(32.0)]
    public void NeedsAudioDisable_TrueAboveLimitOnPlaying(double rate)
    {
        // >4× 且音轨尚未禁用、处于 Playing（音轨已完成选择）时才下发禁用
        Assert.True(VideoPlaybackService.NeedsAudioDisable(rate, alreadyDisabled: false, VLCState.Playing));
    }

    [Fact]
    public void NeedsAudioDisable_FalseAtOrBelowLimit()
    {
        Assert.False(VideoPlaybackService.NeedsAudioDisable(4.0, alreadyDisabled: false, VLCState.Playing));
        Assert.False(VideoPlaybackService.NeedsAudioDisable(1.0, alreadyDisabled: false, VLCState.Playing));
    }

    [Fact]
    public void NeedsAudioDisable_FalseWhenAlreadyDisabled()
    {
        // 重缓冲会反复触发 Playing：已禁用时不再重复下发原生调用
        Assert.False(VideoPlaybackService.NeedsAudioDisable(8.0, alreadyDisabled: true, VLCState.Playing));
    }

    [Theory]
    [InlineData(VLCState.Opening)]
    [InlineData(VLCState.Buffering)]
    [InlineData(VLCState.Paused)]
    [InlineData(VLCState.Stopped)]
    public void NeedsAudioDisable_FalseOutsidePlaying(VLCState state)
    {
        // 音轨未就绪（或已停止）时不尝试禁用，避免误记状态而错过 Playing 重试
        Assert.False(VideoPlaybackService.NeedsAudioDisable(8.0, alreadyDisabled: false, state));
    }
}
