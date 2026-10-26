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

    // --- 相对 seek 基准与合并判定（v1.7.6：UI 线程不再直读原生 Time）---

    [Fact]
    public void ResolveSeekBase_PendingInWindow_UsesPendingTarget()
    {
        // 抑制窗内存在未到位目标：以目标叠加，避免长按累积偏差
        Assert.Equal(60_000, VideoPlaybackService.ResolveSeekBaseMs(
            pendingTargetMs: 60_000, sinceSeekMs: 100, cachedMs: 50_000));
    }

    [Fact]
    public void ResolveSeekBase_NoPending_UsesCachedTime()
    {
        // 无 pending seek：基准取缓存（不触碰原生 Time）
        Assert.Equal(50_000, VideoPlaybackService.ResolveSeekBaseMs(
            pendingTargetMs: -1, sinceSeekMs: 100, cachedMs: 50_000));
    }

    [Fact]
    public void ResolveSeekBase_PendingOutOfWindow_UsesCachedTime()
    {
        // 超抑制窗：目标已失效，回落缓存
        Assert.Equal(50_000, VideoPlaybackService.ResolveSeekBaseMs(
            pendingTargetMs: 60_000, sinceSeekMs: 450, cachedMs: 50_000));
    }

    // --- v1.7.7：暂停态 seek → 立刻截图场景 ---
    // 用户暂停视频、拖动时间轴到新位置、按 X 截图。此时：
    //  ① VLC 暂停，_cachedTimeMs 仍是暂停前的旧值（TimeChanged 不触发）
    //  ② _pendingSeekTargetMs 刚被 SeekToMs 设为目标值
    //  ③ Position getter 应该返回目标值而不是旧缓存
    // ResolveSeekBaseMs 覆盖这个场景：pending 在窗内时返回目标。

    [Fact]
    public void ResolveSeekBase_PausedSeek_UsesTargetImmediately()
    {
        // 暂停态 SeekToMs(60s) 刚执行，settle 窗口内，缓存仍为暂停前的 50s
        // Position getter 必须返回 60s —— 立刻截图才能对得准
        Assert.Equal(60_000, VideoPlaybackService.ResolveSeekBaseMs(
            pendingTargetMs: 60_000, sinceSeekMs: 10, cachedMs: 50_000));
    }

    [Fact]
    public void ResolveSeekBase_PausedSeek_StaleAfterSettleWindow()
    {
        // 暂停态 seek 超窗后 TimeChanged 仍没来（VLC 暂停态不主动推 TimeChanged），
        // pending 目标被视为 stale，回落缓存。实际运行中不会出现——seek 到位后 TimeChanged 会清掉 pending。
        // 这条测试只是确认边界行为。
        Assert.Equal(50_000, VideoPlaybackService.ResolveSeekBaseMs(
            pendingTargetMs: 60_000, sinceSeekMs: 500, cachedMs: 50_000));
    }

    [Fact]
    public void ResolveSeekBase_BurstSeek_UsesLatestTarget()
    {
        // 长按方向键 burst 场景：每次 SeekRelative 目标覆盖上一个，最新目标优先
        Assert.Equal(90_000, VideoPlaybackService.ResolveSeekBaseMs(
            pendingTargetMs: 90_000, sinceSeekMs: 200, cachedMs: 50_000));
    }

    [Fact]
    public void ShouldCoalesceSeek_RecentApply_IsBurst()
    {
        // 距上次下发 < 400ms（长按方向键）：进入合并窗口
        Assert.True(VideoPlaybackService.ShouldCoalesceSeek(50));
        Assert.True(VideoPlaybackService.ShouldCoalesceSeek(399));
    }

    [Fact]
    public void ShouldCoalesceSeek_SingleClick_AppliesImmediately()
    {
        // 单次点击 / 拖动落点：不额外增加延迟
        Assert.False(VideoPlaybackService.ShouldCoalesceSeek(400));
        Assert.False(VideoPlaybackService.ShouldCoalesceSeek(5_000));
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
