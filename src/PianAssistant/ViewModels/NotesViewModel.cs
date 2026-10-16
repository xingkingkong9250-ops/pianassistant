using System.Collections.ObjectModel;
using System.Windows.Input;
using PianAssistant.Helpers;
using PianAssistant.Models;
using PianAssistant.Services;

namespace PianAssistant.ViewModels;

/// <summary>笔记面板筛选模式（W1-C2-D-01）。</summary>
public enum NotesFilter
{
    /// <summary>全部条目。</summary>
    All,
    /// <summary>只显示截图。</summary>
    Screenshots,
    /// <summary>只显示视频片段（含裁切点）。</summary>
    Segments,
    /// <summary>只显示选中项。</summary>
    Selected,
    /// <summary>只显示裁切点（更多筛选下拉）。</summary>
    CutPointsOnly,
}

/// <summary>
/// 侧边笔记面板（W1-C2-D 统一时间线）：截图/片段/裁切点按时间顺序混排。
/// 片段由裁切点自动分段；裁切点可选中删除，删除后相邻片段自动合并。
/// 支持五种筛选与选中导出；时间轴标记（Markers）由截图集合生成。
/// </summary>
public sealed class NotesViewModel : ObservableObject
{
    /// <summary>跳转判定容差：位置差小于该值视为同一时刻。</summary>
    private static readonly TimeSpan PositionEpsilon = TimeSpan.FromMilliseconds(1);

    private readonly Func<TimeSpan> _getPosition;
    private NotesFilter _activeFilter = NotesFilter.All;
    private IReadOnlyList<TimelineListItem> _filtered = Array.Empty<TimelineListItem>();
    private IReadOnlyList<TimeSpan> _cutPoints = Array.Empty<TimeSpan>();

    /// <summary>截图集合（按时间排序；供画廊/会话快照复用）。</summary>
    public ObservableCollection<ScreenshotEntry> Screenshots { get; } = new();

    /// <summary>片段集合（由裁切点自动分段）。</summary>
    public ObservableCollection<TimelineSegment> Segments { get; } = new();

    /// <summary>时间轴标记（仅截图小色块；裁切点已由时间轴红三角表示）。</summary>
    public ObservableCollection<TimelineMarker> Markers { get; } = new();

    /// <summary>统一时间线条目（截图/片段/裁切点混排，Position 升序）。</summary>
    public ObservableCollection<TimelineListItem> Items { get; } = new();

    /// <summary>按当前筛选模式过滤后的条目（供 UI 绑定）。</summary>
    public IReadOnlyList<TimelineListItem> FilteredItems => _filtered;

    /// <summary>片段缩略图服务（可选；设置后 RecomputeSegments 自动补抽段起始帧）。</summary>
    public SegmentThumbnailService? Thumbnails { get; set; }

    public NotesViewModel(Func<TimeSpan> getPosition)
    {
        ArgumentNullException.ThrowIfNull(getPosition);
        _getPosition = getPosition;
        ExportSelectedCommand = new RelayCommand(
            () => ExportSelectedRequested?.Invoke(this, EventArgs.Empty));
        RefreshFilter();
    }

    // --- 筛选 ---

    /// <summary>当前筛选模式。</summary>
    public NotesFilter ActiveFilter
    {
        get => _activeFilter;
        set
        {
            if (SetProperty(ref _activeFilter, value))
                RefreshFilter();
        }
    }

    // 统计
    public int ScreenshotCount => Screenshots.Count;
    public int SegmentCount => Segments.Count;
    public int CutPointCount => _cutPoints.Count;

    /// <summary>数据发生变更（供会话自动保存触发 MarkDirty）。</summary>
    public event EventHandler? Changed;

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    // --- 截图 ---

    public void AddScreenshot(ScreenshotEntry screenshot)
    {
        ArgumentNullException.ThrowIfNull(screenshot);
        var index = Screenshots.TakeWhile(s => s.Position <= screenshot.Position).Count();
        Screenshots.Insert(index, screenshot);
        RebuildItems();
        NotifyCountsChanged();
        RaiseChanged();
    }

    public void RemoveScreenshot(ScreenshotEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Screenshots.Remove(entry);
        RebuildItems();
        OnPropertyChanged(nameof(ScreenshotCount));
        RaiseChanged();
    }

    /// <summary>清空所有数据（打开新视频时调用）。</summary>
    public void ClearAll()
    {
        Screenshots.Clear();
        Segments.Clear();
        _cutPoints = Array.Empty<TimeSpan>();
        RebuildItems();
        NotifyCountsChanged();
        RaiseChanged();
    }

    private void NotifyCountsChanged()
    {
        OnPropertyChanged(nameof(ScreenshotCount));
        OnPropertyChanged(nameof(SegmentCount));
        OnPropertyChanged(nameof(CutPointCount));
    }

    // --- 片段（自动分段）---

    /// <summary>
    /// 根据裁切点重新计算段并重建统一列表。删除裁切点后相邻片段
    /// 随之合并（分段边界重算的自然结果）。设置 <see cref="Thumbnails"/>
    /// 后自动增量补抽段起始帧并清理孤儿缓存。
    /// </summary>
    public void RecomputeSegments(IReadOnlyList<TimeSpan> cutPoints, TimeSpan length, string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(cutPoints);
        _cutPoints = cutPoints.OrderBy(c => c).ToList();
        Segments.Clear();
        if (length > TimeSpan.Zero)
        {
            var boundaries = new List<TimeSpan> { TimeSpan.Zero };
            boundaries.AddRange(_cutPoints.Where(c => c > TimeSpan.Zero && c < length));
            boundaries.Add(length);

            for (int i = 0; i < boundaries.Count - 1; i++)
                Segments.Add(new TimelineSegment(i + 1, boundaries[i], boundaries[i + 1], sourcePath));
        }
        RebuildItems();
        NotifyCountsChanged();
        KickOffThumbnails(sourcePath);
        RaiseChanged();
    }

    // --- 跳转 ---

    public event EventHandler<TimeSpan>? SeekRequested;

    public void JumpTo(TimeSpan position) => SeekRequested?.Invoke(this, position);

    public void JumpTo(TimelineListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        SeekRequested?.Invoke(this, item.Position);
    }

    /// <summary>跳到当前播放位置的上一个条目（[ 键）；无条目或已在最前时不触发。</summary>
    public void JumpToPrevious()
    {
        var current = _getPosition();
        TimelineListItem? target = null;
        foreach (var item in Items) // Items 按时间升序
        {
            if (item.Position < current - PositionEpsilon)
                target = item;
            else
                break;
        }
        if (target is not null)
            SeekRequested?.Invoke(this, target.Position);
    }

    /// <summary>跳到当前播放位置的下一个条目（] 键）；无条目或已在最后时不触发。</summary>
    public void JumpToNext()
    {
        var current = _getPosition();
        foreach (var item in Items)
        {
            if (item.Position > current + PositionEpsilon)
            {
                SeekRequested?.Invoke(this, item.Position);
                return;
            }
        }
    }

    // --- 选中与导出 ---

    /// <summary>当前选中的条目（跨类型，按时间升序）。</summary>
    public IReadOnlyList<TimelineListItem> GetSelectedItems() =>
        Items.Where(i => i.IsSelected).ToList();

    /// <summary>导出选中请求：View 层弹目录选择框后执行复制/切出。</summary>
    public event EventHandler? ExportSelectedRequested;

    public ICommand ExportSelectedCommand { get; }

    // --- 内部 ---

    /// <summary>重建统一列表：截图按时间编号；排序 Position 升序 + 同刻 裁切点→片段→截图。</summary>
    private void RebuildItems()
    {
        // 记住选中态来源（截图/裁切点按位置可恢复；片段随分段重建而重置）
        var selectedShots = Items.OfType<ScreenshotListItem>()
            .Where(i => i.IsSelected).Select(i => i.Entry).ToHashSet();
        var selectedCuts = Items.OfType<CutPointListItem>()
            .Where(i => i.IsSelected).Select(i => i.Position).ToHashSet();

        Items.Clear();
        var rows = new List<TimelineListItem>();
        var number = 0;
        foreach (var s in Screenshots)
            rows.Add(new ScreenshotListItem(s, ++number));
        foreach (var seg in Segments)
            rows.Add(new SegmentListItem(seg));
        foreach (var cp in _cutPoints)
            rows.Add(new CutPointListItem(cp));

        foreach (var row in rows
            .OrderBy(r => r.Position)
            .ThenBy(r => r.KindSortOrder))
        {
            if (row is ScreenshotListItem si && selectedShots.Contains(si.Entry))
                row.IsSelected = true;
            if (row is CutPointListItem ci && selectedCuts.Contains(ci.Position))
                row.IsSelected = true;
            // 选中态变化需实时刷新「只显示选中项」筛选
            row.PropertyChanged += OnItemPropertyChanged;
            Items.Add(row);
        }
        RebuildMarkers();
        RefreshFilter();
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineListItem.IsSelected)
            && _activeFilter == NotesFilter.Selected)
            RefreshFilter();
    }

    /// <summary>时间轴标记只来自截图（裁切点时间轴已有红三角）。</summary>
    private void RebuildMarkers()
    {
        Markers.Clear();
        foreach (var s in Screenshots)
            Markers.Add(new TimelineMarker(s.Position, MarkerKind.Screenshot));
    }

    private void RefreshFilter()
    {
        IEnumerable<TimelineListItem> query = _activeFilter switch
        {
            NotesFilter.Screenshots => Items.Where(i => i.Kind == TimelineItemKind.Screenshot),
            NotesFilter.Segments => Items.Where(i =>
                i.Kind is TimelineItemKind.Segment or TimelineItemKind.CutPoint),
            NotesFilter.Selected => Items.Where(i => i.IsSelected),
            NotesFilter.CutPointsOnly => Items.Where(i => i.Kind == TimelineItemKind.CutPoint),
            _ => Items,
        };
        _filtered = query.ToList();
        OnPropertyChanged(nameof(FilteredItems));
    }

    /// <summary>增量补抽段起始帧缩略图并清理孤儿缓存（后台执行，不阻塞列表）。</summary>
    private void KickOffThumbnails(string? sourcePath)
    {
        var svc = Thumbnails;
        if (svc is null) return;
        // 孤儿缓存清理不依赖源视频是否打开
        svc.PruneOrphans(Segments.Select(s => (long)s.Start.TotalMilliseconds).ToHashSet());
        if (string.IsNullOrWhiteSpace(sourcePath)) return;
        foreach (var item in Items.OfType<SegmentListItem>())
        {
            var captured = item;
            var start = captured.Segment.Start;
            _ = Task.Run(async () =>
            {
                var path = await svc.EnsureThumbnailAsync(sourcePath, start);
                if (path is not null)
                    captured.ThumbnailPath = path;
            });
        }
    }
}
