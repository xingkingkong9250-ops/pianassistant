using System.Collections.ObjectModel;
using PianAssistant.Helpers;
using PianAssistant.Models;

namespace PianAssistant.ViewModels;

/// <summary>
/// 侧边笔记面板：顶部截图/片段两个子标签页。
/// 截图标签按时间排序显示缩略图；片段标签根据裁切点自动分段、编号、标出时长。
/// 同时保留活动流（Activities）记录所有操作历史。
/// </summary>
public sealed class NotesViewModel : ObservableObject
{
    private readonly Func<TimeSpan> _getPosition;
    private string _newNoteText = string.Empty;
    private string _searchText = string.Empty;
    private NotesSubTab _activeSubTab = NotesSubTab.Screenshots;

    /// <summary>截图集合（按时间排序）。</summary>
    public ObservableCollection<ScreenshotEntry> Screenshots { get; } = new();

    /// <summary>片段集合（由裁切点自动分段）。</summary>
    public ObservableCollection<TimelineSegment> Segments { get; } = new();

    /// <summary>活动流（所有操作历史）。</summary>
    public ObservableCollection<ActivityEntry> Activities { get; } = new();

    public string NewNoteText
    {
        get => _newNoteText;
        set => SetProperty(ref _newNoteText, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    /// <summary>当前子标签页。</summary>
    public NotesSubTab ActiveSubTab
    {
        get => _activeSubTab;
        set => SetProperty(ref _activeSubTab, value);
    }

    // 统计
    public int ScreenshotCount => Screenshots.Count;
    public int SegmentCount => Segments.Count;
    public int CutPointCount => Activities.Count(a => a.Type == ActivityType.CutPoint);

    public NotesViewModel(Func<TimeSpan> getPosition)
    {
        ArgumentNullException.ThrowIfNull(getPosition);
        _getPosition = getPosition;
    }

    // --- 截图 ---

    public void AddScreenshot(ScreenshotEntry screenshot)
    {
        ArgumentNullException.ThrowIfNull(screenshot);
        var index = Screenshots.TakeWhile(s => s.Position <= screenshot.Position).Count();
        Screenshots.Insert(index, screenshot);
        var label = $"截图 {screenshot.Position:hh\\:mm\\:ss}";
        var entry = new ActivityEntry(ActivityType.Screenshot, screenshot.Position, label,
            screenshot.OutputPath);
        InsertActivitySorted(entry);
        NotifyCountsChanged();
    }

    /// <summary>清空所有数据（打开新视频时调用）。</summary>
    public void ClearAll()
    {
        Screenshots.Clear();
        Segments.Clear();
        Activities.Clear();
        NotifyCountsChanged();
    }

    private void NotifyCountsChanged()
    {
        OnPropertyChanged(nameof(ScreenshotCount));
        OnPropertyChanged(nameof(SegmentCount));
        OnPropertyChanged(nameof(CutPointCount));
    }

    public void RemoveScreenshot(ScreenshotEntry entry)
    {
        Screenshots.Remove(entry);
        OnPropertyChanged(nameof(ScreenshotCount));
    }

    // --- 片段（自动分段）---

    /// <summary>根据裁切点重新计算段。</summary>
    public void RecomputeSegments(IReadOnlyList<TimeSpan> cutPoints, TimeSpan length, string? sourcePath)
    {
        Segments.Clear();
        if (length <= TimeSpan.Zero) return;

        var boundaries = new List<TimeSpan> { TimeSpan.Zero };
        boundaries.AddRange(cutPoints.Where(c => c > TimeSpan.Zero && c < length).OrderBy(c => c));
        boundaries.Add(length);

        for (int i = 0; i < boundaries.Count - 1; i++)
        {
            var seg = new TimelineSegment(i + 1, boundaries[i], boundaries[i + 1], sourcePath);
            Segments.Add(seg);
        }
        OnPropertyChanged(nameof(SegmentCount));
    }

    // --- 活动流 ---

    public void AddNote(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(_newNoteText)) return;
        var label = _newNoteText.Trim();
        var entry = new ActivityEntry(ActivityType.Note, _getPosition(), label);
        InsertActivitySorted(entry);
        NewNoteText = string.Empty;
    }

    public void AddClip(ClipRange clip, string? outputPath)
    {
        ArgumentNullException.ThrowIfNull(clip);
        var label = $"片段 {clip.InPoint:hh\\:mm\\:ss}→{clip.OutPoint:hh\\:mm\\:ss}";
        var entry = new ActivityEntry(ActivityType.Clip, clip.InPoint, label,
            outputPath, clip.Duration);
        InsertActivitySorted(entry);
    }

    public void AddCutPoint(TimeSpan position)
    {
        var label = $"裁切点 {position:hh\\:mm\\:ss}";
        var entry = new ActivityEntry(ActivityType.CutPoint, position, label);
        InsertActivitySorted(entry);
        OnPropertyChanged(nameof(CutPointCount));
    }

    // --- 跳转 ---

    public event EventHandler<TimeSpan>? SeekRequested;

    public void JumpTo(TimeSpan position)
    {
        SeekRequested?.Invoke(this, position);
    }

    public void JumpTo(ActivityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        SeekRequested?.Invoke(this, entry.Position);
    }

    // --- 内部 ---

    private void InsertActivitySorted(ActivityEntry entry)
    {
        var index = Activities.TakeWhile(a => a.Position <= entry.Position).Count();
        Activities.Insert(index, entry);
    }
}

/// <summary>笔记面板子标签页。</summary>
public enum NotesSubTab
{
    Screenshots,
    Segments,
}
