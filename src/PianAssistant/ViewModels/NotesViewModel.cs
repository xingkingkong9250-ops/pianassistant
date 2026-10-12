using System.Collections.ObjectModel;
using System.Windows.Input;
using PianAssistant.Helpers;
using PianAssistant.Models;
using PianAssistant.Services;

namespace PianAssistant.ViewModels;

/// <summary>
/// 侧边笔记面板：顶部截图/片段/活动流三个子标签页。
/// 截图标签按时间排序显示缩略图；片段标签根据裁切点自动分段、编号、标出时长；
/// 活动流记录所有操作历史，支持搜索、时间范围过滤、笔记编辑/删除与导出。
/// 同时为时间轴维护标记集合（Markers，笔记/截图/片段小色块）。
/// </summary>
public sealed class NotesViewModel : ObservableObject
{
    /// <summary>跳转判定容差：位置差小于该值视为同一时刻。</summary>
    private static readonly TimeSpan PositionEpsilon = TimeSpan.FromMilliseconds(1);

    private readonly Func<TimeSpan> _getPosition;
    private string _newNoteText = string.Empty;
    private string _searchText = string.Empty;
    private NotesSubTab _activeSubTab = NotesSubTab.Screenshots;
    private NoteTemplate _activeTemplate = NoteTemplate.None;
    private TimeSpan? _filterStart;
    private TimeSpan? _filterEnd;
    private IReadOnlyList<ActivityEntry> _filtered = Array.Empty<ActivityEntry>();

    /// <summary>截图集合（按时间排序）。</summary>
    public ObservableCollection<ScreenshotEntry> Screenshots { get; } = new();

    /// <summary>片段集合（由裁切点自动分段）。</summary>
    public ObservableCollection<TimelineSegment> Segments { get; } = new();

    /// <summary>活动流（所有操作历史）。</summary>
    public ObservableCollection<ActivityEntry> Activities { get; } = new();

    /// <summary>
    /// 时间轴标记（笔记/截图/片段小色块；裁切点已由时间轴红三角表示，不重复绘制）。
    /// 随 Activities 变化自动重建。
    /// </summary>
    public ObservableCollection<TimelineMarker> Markers { get; } = new();

    /// <summary>内置笔记模板（静态，供其他入口复用）。</summary>
    public static IReadOnlyList<NoteTemplate> Templates { get; } = NoteTemplate.Presets;

    /// <summary>模板下拉框选项：「无模板」+ 六个预设。</summary>
    public IReadOnlyList<NoteTemplate> TemplateOptions { get; } = BuildTemplateOptions();

    private static IReadOnlyList<NoteTemplate> BuildTemplateOptions()
    {
        var list = new List<NoteTemplate> { NoteTemplate.None };
        list.AddRange(NoteTemplate.Presets);
        return list;
    }

    /// <summary>当前激活的笔记模板（AddNote 时自动加前缀）。</summary>
    public NoteTemplate ActiveTemplate
    {
        get => _activeTemplate;
        set => SetProperty(ref _activeTemplate, value ?? NoteTemplate.None);
    }

    public string NewNoteText
    {
        get => _newNoteText;
        set => SetProperty(ref _newNoteText, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                RefreshFilter();
        }
    }

    /// <summary>活动流时间范围筛选起点（null=不限）。</summary>
    public TimeSpan? FilterStart
    {
        get => _filterStart;
        set
        {
            if (SetProperty(ref _filterStart, value))
                RefreshFilter();
        }
    }

    /// <summary>活动流时间范围筛选终点（null=不限）。</summary>
    public TimeSpan? FilterEnd
    {
        get => _filterEnd;
        set
        {
            if (SetProperty(ref _filterEnd, value))
                RefreshFilter();
        }
    }

    /// <summary>按搜索文本与时间范围过滤后的活动流（供 UI 绑定与导出）。</summary>
    public IReadOnlyList<ActivityEntry> FilteredActivities => _filtered;

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
        // 活动流任何变化 → 同步刷新过滤结果与时间轴标记
        Activities.CollectionChanged += (_, _) =>
        {
            RefreshFilter();
            RebuildMarkers();
        };
        RefreshFilter();
        // 导出由 View 弹文件对话框，VM 只发请求
        ExportCommand = new RelayCommand(() => ExportRequested?.Invoke(this, EventArgs.Empty));
        // 整理导出（结构化 Markdown 报告）同样由 View 弹对话框
        ExportReportCommand = new RelayCommand(() => ExportReportRequested?.Invoke(this, EventArgs.Empty));
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
        // 应用当前模板前缀（「无模板」为空前缀）
        var label = _activeTemplate.Prefix + _newNoteText.Trim();
        var entry = new ActivityEntry(ActivityType.Note, _getPosition(), label);
        InsertActivitySorted(entry);
        NewNoteText = string.Empty;
    }

    /// <summary>重命名笔记条目（活动流内联编辑）；仅对 Note 类型生效。</summary>
    public void RenameNote(ActivityEntry entry, string newText)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Type != ActivityType.Note) return;
        var text = newText?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        entry.Label = text;
        entry.IsEditing = false;
        // 标签变化不影响 CollectionChanged，需手动刷新过滤结果
        RefreshFilter();
    }

    /// <summary>删除活动条目（笔记/截图/片段记录均可删除）。</summary>
    public void Remove(ActivityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // CollectionChanged 会自动同步过滤结果与时间轴标记
        Activities.Remove(entry);
        NotifyCountsChanged();
    }

    /// <summary>
    /// 导出活动流请求：View 层弹出 SaveFileDialog 选路径后调用
    /// <see cref="NotesExportService.Export"/>（按扩展名决定 Markdown/TXT）。
    /// </summary>
    public event EventHandler? ExportRequested;

    public ICommand ExportCommand { get; }

    /// <summary>
    /// 整理导出请求（M5）：View 层弹出 SaveFileDialog 选路径后调用
    /// <see cref="ExportActivityReport"/> 输出结构化 Markdown 报告。
    /// </summary>
    public event EventHandler? ExportReportRequested;

    public ICommand ExportReportCommand { get; }

    /// <summary>
    /// 整理导出：把全部活动流（按位置排序）与分段汇总写成结构化 Markdown 报告。
    /// </summary>
    public void ExportActivityReport(string path, string videoTitle) =>
        NotesExportService.ExportActivityReport(Activities, Segments, path, videoTitle);

    /// <summary>批量指令分发：在指定时间位置直接添加一条笔记活动条目。</summary>
    public void AddMarkerAt(string text, TimeSpan position)
    {
        var label = string.IsNullOrWhiteSpace(text) ? "标记" : text.Trim();
        var entry = new ActivityEntry(ActivityType.Note, position, label);
        InsertActivitySorted(entry);
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

    /// <summary>
    /// 跳到当前播放位置的上一个活动条目。
    /// 边界：无条目或已在最前时不触发跳转。
    /// </summary>
    public void JumpToPrevious()
    {
        var current = _getPosition();
        ActivityEntry? target = null;
        foreach (var a in Activities) // Activities 按时间升序
        {
            if (a.Position < current - PositionEpsilon)
                target = a;
            else
                break;
        }
        if (target is not null)
            SeekRequested?.Invoke(this, target.Position);
    }

    /// <summary>
    /// 跳到当前播放位置的下一个活动条目。
    /// 边界：无条目或已在最后时不触发跳转。
    /// </summary>
    public void JumpToNext()
    {
        var current = _getPosition();
        foreach (var a in Activities) // Activities 按时间升序
        {
            if (a.Position > current + PositionEpsilon)
            {
                SeekRequested?.Invoke(this, a.Position);
                return;
            }
        }
    }

    // --- 过滤与标记 ---

    /// <summary>按搜索文本与时间范围重建过滤结果。</summary>
    private void RefreshFilter()
    {
        IEnumerable<ActivityEntry> query = Activities;
        var text = _searchText?.Trim();
        if (!string.IsNullOrEmpty(text))
            query = query.Where(a => a.Label.Contains(text, StringComparison.OrdinalIgnoreCase));
        if (_filterStart is { } start)
            query = query.Where(a => a.Position >= start);
        if (_filterEnd is { } end)
            query = query.Where(a => a.Position <= end);
        _filtered = query.ToList();
        OnPropertyChanged(nameof(FilteredActivities));
    }

    /// <summary>根据活动流重建时间轴标记（裁切点不生成，时间轴已有红三角）。</summary>
    private void RebuildMarkers()
    {
        Markers.Clear();
        foreach (var a in Activities)
        {
            MarkerKind? kind = a.Type switch
            {
                ActivityType.Note => MarkerKind.Note,
                ActivityType.Screenshot => MarkerKind.Screenshot,
                ActivityType.Clip => MarkerKind.Clip,
                _ => null, // CutPoint：时间轴已用红色三角绘制
            };
            if (kind is { } k)
                Markers.Add(new TimelineMarker(a.Position, k));
        }
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
    Activities,
}
