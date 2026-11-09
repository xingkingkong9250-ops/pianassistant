using System.Collections.ObjectModel;
using System.Windows.Input;
using PianAssistant.Helpers;
using PianAssistant.Models;
using PianAssistant.Services;

namespace PianAssistant.ViewModels;

/// <summary>笔记面板基筛选模式（W1-C2-D-01；与「仅标记」叠加开关组合使用）。</summary>
public enum NotesFilter
{
    /// <summary>全部条目。</summary>
    All,
    /// <summary>只显示截图。</summary>
    Screenshots,
    /// <summary>只显示视频片段（含裁切点）。</summary>
    Segments,
    /// <summary>只显示裁切点。</summary>
    CutPointsOnly,
}

/// <summary>
/// 侧边笔记面板（W1-C2-D 统一时间线）：截图/片段/裁切点按时间顺序混排。
/// 片段由裁切点自动分段；裁切点可选中删除，删除后相邻片段自动合并。
/// 筛选 = 基筛选（全部/截图/片段/裁切点）+「仅标记」叠加开关；
/// 勾选标记（IsMarked）为独立记录信息，用于批量导出与叠加筛选；
/// 时间轴标记（Markers）由截图集合生成。
/// </summary>
public sealed class NotesViewModel : ObservableObject
{
    /// <summary>跳转判定容差：位置差小于该值视为同一时刻。</summary>
    private static readonly TimeSpan PositionEpsilon = TimeSpan.FromMilliseconds(1);

    private readonly Func<TimeSpan> _getPosition;
    private NotesFilter _activeFilter = NotesFilter.All;
    private bool _onlyMarked;
    private IReadOnlyList<TimelineListItem> _filtered = Array.Empty<TimelineListItem>();
    private IReadOnlyList<TimeSpan> _cutPoints = Array.Empty<TimeSpan>();

    /// <summary>截图集合（按时间排序；供画廊/会话快照复用）。</summary>
    public ObservableCollection<ScreenshotEntry> Screenshots { get; } = new();

    /// <summary>片段集合（由裁切点自动分段）。</summary>
    public ObservableCollection<TimelineSegment> Segments { get; } = new();

    /// <summary>时间轴标记（仅截图小色块；裁切点已由时间轴红三角表示）。</summary>
    public ObservableCollection<TimelineMarker> Markers { get; } = new();

    /// <summary>已勾选标记的片段集合（时间轴黄色带渲染源，与 IsMarked 同一数据源）。</summary>
    public ObservableCollection<TimelineSegment> MarkedSegments { get; } = new();

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
        ExportMarkedCommand = new RelayCommand(
            () => ExportMarkedRequested?.Invoke(this, EventArgs.Empty));
        ExportDataCommand = new RelayCommand(
            () => ExportDataRequested?.Invoke(this, EventArgs.Empty));
        ImportDataCommand = new RelayCommand(
            () => ImportDataRequested?.Invoke(this, EventArgs.Empty));
        RefreshFilter();
    }

    // --- 筛选 ---

    /// <summary>当前基筛选模式。</summary>
    public NotesFilter ActiveFilter
    {
        get => _activeFilter;
        set
        {
            if (SetProperty(ref _activeFilter, value))
                RefreshFilter();
        }
    }

    /// <summary>「仅标记」叠加开关：开启后在基筛选结果上再只保留已勾选标记的条目。</summary>
    public bool OnlyMarked
    {
        get => _onlyMarked;
        set
        {
            if (SetProperty(ref _onlyMarked, value))
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

    /// <summary>
    /// 批量清空全部截图（清空全部标记用）：重建列表（截图行移除、片段保留）
    /// 与时间轴标记（Markers 随截图记录清空），png 文件由调用方决定是否保留。
    /// </summary>
    public void ClearScreenshots()
    {
        if (Screenshots.Count == 0) return;
        Screenshots.Clear();
        RebuildItems();
        NotifyCountsChanged();
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

    // --- 标记与导出 ---

    /// <summary>当前选中的条目（ListBox 行选中，跨类型；用于 Delete 删裁切点等）。</summary>
    public IReadOnlyList<TimelineListItem> GetSelectedItems() =>
        Items.Where(i => i.IsSelected).ToList();

    /// <summary>当前勾选标记的条目（跨类型，按时间升序；用于批量导出）。</summary>
    public IReadOnlyList<TimelineListItem> GetMarkedItems() =>
        Items.Where(i => i.IsMarked).ToList();

    /// <summary>导出标记请求：View 层弹目录选择框后执行复制/切出。</summary>
    public event EventHandler? ExportMarkedRequested;

    public ICommand ExportMarkedCommand { get; }

    /// <summary>导出数据请求：View 层弹保存框后写 CSV。</summary>
    public event EventHandler? ExportDataRequested;

    public ICommand ExportDataCommand { get; }

    /// <summary>导入数据请求：View 层弹打开框后解析 CSV 并覆盖。</summary>
    public event EventHandler? ImportDataRequested;

    public ICommand ImportDataCommand { get; }

    /// <summary>
    /// 构建「导出数据」快照（W1-C2-D-02）：裁切点、自动分段、截图清单
    /// 及各条目的勾选标记状态。时间码 HH:mm:ss.fff。
    /// </summary>
    public NotesExportData BuildExportData(string? sourcePath, TimeSpan length)
    {
        var data = new NotesExportData
        {
            SourcePath = sourcePath,
            ExportedAt = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss"),
            VideoLength = length > TimeSpan.Zero ? FormatLong(length) : null,
        };
        foreach (var cp in _cutPoints.OrderBy(c => c))
            data.CutPoints.Add(FormatLong(cp));
        foreach (var seg in Items.OfType<SegmentListItem>())
            data.Segments.Add(new ExportSegment
            {
                Index = seg.Segment.Index,
                Start = FormatLong(seg.Segment.Start),
                End = FormatLong(seg.Segment.End),
                Marked = seg.IsMarked,
            });
        foreach (var shot in Items.OfType<ScreenshotListItem>())
            data.Screenshots.Add(new ExportScreenshot
            {
                Position = FormatLong(shot.Entry.Position),
                File = shot.Entry.OutputPath,
                Marked = shot.IsMarked,
            });
        return data;
    }

    /// <summary>HH:mm:ss.fff（超一小时也能完整表达）。</summary>
    private static string FormatLong(TimeSpan ts) => ts.ToString(@"hh\:mm\:ss\.fff");

    // --- 内部 ---

    /// <summary>重建统一列表：截图按时间编号；排序 Position 升序 + 同刻 裁切点→片段→截图。</summary>
    private void RebuildItems()
    {
        // 记住选中/标记态来源（截图按 Entry、裁切点按位置、片段按起止时间码恢复；
        // 勾选标记是用户的独立记录信息，重建后必须保留）
        var selectedShots = Items.OfType<ScreenshotListItem>()
            .Where(i => i.IsSelected).Select(i => i.Entry).ToHashSet();
        var selectedCuts = Items.OfType<CutPointListItem>()
            .Where(i => i.IsSelected).Select(i => i.Position).ToHashSet();
        var markedShots = Items.OfType<ScreenshotListItem>()
            .Where(i => i.IsMarked).Select(i => i.Entry).ToHashSet();
        var markedSegs = Items.OfType<SegmentListItem>()
            .Where(i => i.IsMarked).Select(i => (i.Segment.Start, i.Segment.End)).ToHashSet();
        var markedCuts = Items.OfType<CutPointListItem>()
            .Where(i => i.IsMarked).Select(i => i.Position).ToHashSet();

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
            if (row is ScreenshotListItem si)
            {
                if (selectedShots.Contains(si.Entry)) si.IsSelected = true;
                if (markedShots.Contains(si.Entry)) si.IsMarked = true;
            }
            else if (row is SegmentListItem gi)
            {
                if (markedSegs.Contains((gi.Segment.Start, gi.Segment.End))) gi.IsMarked = true;
            }
            else if (row is CutPointListItem ci)
            {
                if (selectedCuts.Contains(ci.Position)) ci.IsSelected = true;
                if (markedCuts.Contains(ci.Position)) ci.IsMarked = true;
            }
            // 选中/标记态变化需实时刷新叠加筛选
            row.PropertyChanged += OnItemPropertyChanged;
            row.IsMarkedChanging += OnItemIsMarkedChanging;
            Items.Add(row);
        }
        RebuildMarkers();
        RebuildMarkedSegments();
        RefreshFilter();
        // 诊断：记录重建后标记数（v1.15.5 排查标记丢失）
        var afterShots = Items.OfType<ScreenshotListItem>().Count(i => i.IsMarked);
        var afterSegs = Items.OfType<SegmentListItem>().Count(i => i.IsMarked);
        var afterCuts = Items.OfType<CutPointListItem>().Count(i => i.IsMarked);
        if (markedShots.Count != afterShots || markedSegs.Count != afterSegs || markedCuts.Count != afterCuts)
        {
            AppLog.Info($"[NotesVM] RebuildItems mark delta: shots {markedShots.Count}->{afterShots}, " +
                        $"segs {markedSegs.Count}->{afterSegs}, cuts {markedCuts.Count}->{afterCuts}");
        }
    }

    /// <summary>
    /// 条目勾选标记即将变化（撤回快照用，v1.6.0）：仅用户勾选路径触发；
    /// 列表重建期的标记恢复在事件订阅之前完成，不会触发本事件。
    /// </summary>
    public event EventHandler? ItemMarkToggling;

    private void OnItemIsMarkedChanging(object? sender, EventArgs e) =>
        ItemMarkToggling?.Invoke(this, EventArgs.Empty);

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 笔记列表行选中态 → 增量更新对应 TimelineMarker（v1.15.1）
        if (e.PropertyName == nameof(TimelineListItem.IsSelected) && sender is ScreenshotListItem shotItem)
        {
            SyncMarkerState(shotItem);
            return;
        }
        if (e.PropertyName != nameof(TimelineListItem.IsMarked)) return;
        // 诊断：记录每次标记变化（v1.15.5 排查标记丢失）
        var kind = sender switch
        {
            ScreenshotListItem s => $"shot pos={s.Position:hh\\:mm\\:ss\\.fff}",
            SegmentListItem g => $"seg {g.Segment.Start:hh\\:mm\\:ss\\.fff}-{g.Segment.End:hh\\:mm\\:ss\\.fff}",
            CutPointListItem c => $"cut {c.Position:hh\\:mm\\:ss\\.fff}",
            _ => "?"
        };
        AppLog.Info($"[NotesVM] IsMarked changed: {kind} -> {((TimelineListItem)sender!).IsMarked}");
        RaiseChanged(); // 勾选标记是记录信息，触发会话自动保存
        if (sender is SegmentListItem segItem)
            SyncMarkedSegment(segItem);
        if (sender is ScreenshotListItem shot)
            SyncMarkerState(shot);
        if (_onlyMarked)
            RefreshFilter();
    }

    /// <summary>
    /// 增量同步单个截图条目的 IsSelected / IsMarked 到时间轴 TimelineMarker（v1.15.1）。
    /// TimelineMarker 变可观察，属性 set 会触发 TimelineControl.OnRender 刷新。
    /// </summary>
    private void SyncMarkerState(ScreenshotListItem item)
    {
        var marker = Markers.FirstOrDefault(m => m.Position == item.Position);
        if (marker is null) return;
        marker.IsSelected = item.IsSelected;
        marker.IsMarked = item.IsMarked;
    }

    /// <summary>增量同步时间轴黄带集合：勾选加入（按 Start 排序）、取消移除。</summary>
    private void SyncMarkedSegment(SegmentListItem item)
    {
        MarkedSegments.Remove(item.Segment);
        if (item.IsMarked)
        {
            var index = MarkedSegments.TakeWhile(s => s.Start <= item.Segment.Start).Count();
            MarkedSegments.Insert(index, item.Segment);
        }
    }

    /// <summary>全量重建时间轴黄带集合（随统一列表重建调用）。</summary>
    private void RebuildMarkedSegments()
    {
        MarkedSegments.Clear();
        foreach (var seg in Items.OfType<SegmentListItem>()
            .Where(i => i.IsMarked).Select(i => i.Segment).OrderBy(s => s.Start))
            MarkedSegments.Add(seg);
    }

    /// <summary>
    /// 全量重建时间轴截图标记（v1.15.1：同时从笔记列表同步 IsSelected/IsMarked 状态）。
    /// Screenshots 是数据源，状态在 Items 的 ScreenshotListItem 上。
    /// </summary>
    private void RebuildMarkers()
    {
        Markers.Clear();
        foreach (var s in Screenshots)
        {
            var marker = new TimelineMarker(s.Position, MarkerKind.Screenshot);
            var item = Items.OfType<ScreenshotListItem>()
                .FirstOrDefault(i => i.Position == s.Position);
            if (item is not null)
            {
                marker.IsSelected = item.IsSelected;
                marker.IsMarked = item.IsMarked;
            }
            Markers.Add(marker);
        }
    }

    private void RefreshFilter()
    {
        IEnumerable<TimelineListItem> query = _activeFilter switch
        {
            NotesFilter.Screenshots => Items.Where(i => i.Kind == TimelineItemKind.Screenshot),
            NotesFilter.Segments => Items.Where(i =>
                i.Kind is TimelineItemKind.Segment or TimelineItemKind.CutPoint),
            NotesFilter.CutPointsOnly => Items.Where(i => i.Kind == TimelineItemKind.CutPoint),
            _ => Items,
        };
        // 「仅标记」叠加开关：在基筛选结果上再过滤已勾选标记的条目
        if (_onlyMarked)
            query = query.Where(i => i.IsMarked);
        _filtered = query.ToList();
        OnPropertyChanged(nameof(FilteredItems));
    }

    /// <summary>增量补抽段起始帧缩略图并清理孤儿缓存（后台执行，不阻塞列表）。</summary>
    private void KickOffThumbnails(string? sourcePath)
    {
        var svc = Thumbnails;
        if (svc is null || string.IsNullOrWhiteSpace(sourcePath)) return;
        var valid = Segments.Select(s => (long)s.Start.TotalMilliseconds).ToHashSet();
        // v1.15.8：孤儿清理涉及目录遍历与删除，放到后台，避免大目录时阻塞 UI
        _ = Task.Run(() => svc.PruneOrphans(sourcePath, valid));
        foreach (var item in Items.OfType<SegmentListItem>())
        {
            var captured = item;
            if (!string.IsNullOrEmpty(captured.ThumbnailPath)) continue; // 已有缩略图不重复排队
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
