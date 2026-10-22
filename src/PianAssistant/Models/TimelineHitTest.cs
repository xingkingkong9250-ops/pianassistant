namespace PianAssistant.Models;

/// <summary>
/// 时间轴标记拖拽（v1.7.0）的纯函数：命中检测与移动钳制。
/// 与 UI 解耦，便于单元测试。
/// </summary>
public static class TimelineHitTest
{
    /// <summary>在位置集合中找距 target 最近的下标；最近距离超出容差（毫秒）时返回 -1。</summary>
    public static int FindNearest(IReadOnlyList<TimeSpan> positions, TimeSpan target, double toleranceMs)
    {
        var best = -1;
        var bestMs = double.MaxValue;
        for (var i = 0; i < positions.Count; i++)
        {
            var ms = Math.Abs((positions[i] - target).TotalMilliseconds);
            if (ms < bestMs) { bestMs = ms; best = i; }
        }
        return bestMs <= toleranceMs ? best : -1;
    }

    /// <summary>
    /// 钳制裁切点移动目标：不越过相邻裁切点（保持 minGapMs 最小间距），并限制在 [0, length] 内。
    /// </summary>
    public static TimeSpan ClampCutPointMove(
        IReadOnlyList<TimeSpan> sortedPoints, int index, TimeSpan target, TimeSpan length,
        double minGapMs = 100)
    {
        var gap = TimeSpan.FromMilliseconds(minGapMs);
        var min = index > 0 ? sortedPoints[index - 1] + gap : TimeSpan.Zero;
        var max = index < sortedPoints.Count - 1 ? sortedPoints[index + 1] - gap : length;
        if (max < min) max = min;
        if (target < min) return min;
        if (target > max) return max;
        return target;
    }
}
