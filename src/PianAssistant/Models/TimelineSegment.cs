using PianAssistant.Helpers;

namespace PianAssistant.Models;

/// <summary>
/// 时间轴段：由裁切点将视频自动分段。
/// 例如 3 个裁切点 → 4 段（0→t1, t1→t2, t2→t3, t3→结尾）。
/// </summary>
public sealed class TimelineSegment : ObservableObject
{
    public int Index { get; }              // 1-based 编号
    public TimeSpan Start { get; }         // 段起始
    public TimeSpan End { get; }           // 段结束
    public TimeSpan Duration => End - Start;
    public string? SourcePath { get; }     // 源视频路径

    private bool _isExporting;
    public bool IsExporting
    {
        get => _isExporting;
        set => SetProperty(ref _isExporting, value);
    }

    public TimelineSegment(int index, TimeSpan start, TimeSpan end, string? sourcePath = null)
    {
        Index = index;
        Start = start;
        End = end;
        SourcePath = sourcePath;
    }

    /// <summary>起始时间文本（mm:ss.fff）。</summary>
    public string StartText => Start.ToString(@"mm\:ss\.fff");

    /// <summary>结束时间文本。</summary>
    public string EndText => End.ToString(@"mm\:ss\.fff");

    /// <summary>时长文本。</summary>
    public string DurationText => Duration.ToString(@"mm\:ss\.fff");

    /// <summary>编号+时长摘要。</summary>
    public string Summary => $"#{Index} {DurationText}";
}
