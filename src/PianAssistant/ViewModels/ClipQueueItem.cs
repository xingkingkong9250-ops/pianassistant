using PianAssistant.Helpers;
using PianAssistant.Models;

namespace PianAssistant.ViewModels;

/// <summary>
/// 批量导出队列项：包装 <see cref="ClipRange"/> 并跟踪导出状态
/// （等待 / 导出中 x% / 完成 / 失败 / 已取消）。
/// </summary>
public sealed class ClipQueueItem : ObservableObject
{
    private string _status = "等待";
    private double _progress;

    public ClipQueueItem(ClipRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        Range = range;
    }

    public ClipRange Range { get; }

    /// <summary>入点 → 出点显示文本（无出点时出点显示 --）。</summary>
    public string RangeText =>
        Range.OutPoint is { } outPoint
            ? $"{Range.InPoint:hh\\:mm\\:ss} → {outPoint:hh\\:mm\\:ss}"
            : $"{Range.InPoint:hh\\:mm\\:ss} → --";

    /// <summary>时长显示文本（不完整时为 --）。</summary>
    public string DurationText => Range.Duration?.ToString("hh\\:mm\\:ss") ?? "--";

    /// <summary>片段标签（可为空）。</summary>
    public string? Label => Range.Label;

    /// <summary>状态文本：等待 / 导出中 42% / 完成 / 失败 / 已取消。</summary>
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    /// <summary>当前项导出进度 0..1（绑定行内进度条）。</summary>
    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }
}
