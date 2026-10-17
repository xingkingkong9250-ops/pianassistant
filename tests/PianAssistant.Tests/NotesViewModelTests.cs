using System.IO;
using PianAssistant.Models;
using PianAssistant.ViewModels;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// 笔记面板统一时间线（W1-C2-D）：截图/片段/裁切点混排、
/// 筛选、删裁切点合并片段、选中导出、[ ] 键基于统一列表跳转。
/// </summary>
public class NotesViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private TimeSpan _current = TimeSpan.Zero;
    private readonly NotesViewModel _vm;

    public NotesViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-notes-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _vm = new NotesViewModel(() => _current);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private ScreenshotEntry MakeScreenshot(TimeSpan pos) =>
        new("video.mp4", Path.Combine(_tempDir, $"shot_{pos.TotalMilliseconds}.png"), pos, DateTime.Now);

    private void RecomputeWithCuts(params double[] cutSeconds) =>
        _vm.RecomputeSegments(
            cutSeconds.Select(TimeSpan.FromSeconds).ToList(),
            TimeSpan.FromMinutes(5), "video.mp4");

    // --- 统一列表构建与排序 ---

    [Fact]
    public void Items_MixedSources_SortedByPositionThenKind()
    {
        // 裁切点 10s/20s → 片段 0-10、10-20、20-300
        RecomputeWithCuts(10, 20);
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(10))); // 与裁切点同刻
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(15)));

        var kinds = _vm.Items.Select(i => i.Kind).ToList();
        var positions = _vm.Items.Select(i => i.Position).ToList();

        // Position 升序
        Assert.Equal(positions.OrderBy(p => p).ToList(), positions);
        // 10s 同刻次序：裁切点 → 片段(10-20) → 截图
        var atTen = _vm.Items.Where(i => i.Position == TimeSpan.FromSeconds(10))
            .Select(i => i.Kind).ToList();
        Assert.Equal(
            new[] { TimelineItemKind.CutPoint, TimelineItemKind.Segment, TimelineItemKind.Screenshot },
            atTen);
        // 总数：2 截图 + 3 片段 + 2 裁切点
        Assert.Equal(7, _vm.Items.Count);
    }

    [Fact]
    public void Items_Screenshots_NumberedByTimeOrder()
    {
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(30)));
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(10)));

        var shots = _vm.Items.OfType<ScreenshotListItem>().ToList();
        Assert.Equal("截图 1", shots[0].Title);
        Assert.Equal("截图 2", shots[1].Title);
        Assert.Equal(TimeSpan.FromSeconds(10), shots[0].Position);
    }

    [Fact]
    public void Items_Segments_NamedWithIndexAndDuration()
    {
        RecomputeWithCuts(60);
        var seg = _vm.Items.OfType<SegmentListItem>().First();
        Assert.Equal("片段 1 · 01:00.000", seg.Title);
    }

    // --- 筛选 ---

    [Fact]
    public void Filter_All_ShowsEverything()
    {
        RecomputeWithCuts(10);
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));
        _vm.ActiveFilter = NotesFilter.All;
        Assert.Equal(4, _vm.FilteredItems.Count); // 2 片段 + 1 裁切点 + 1 截图
    }

    [Fact]
    public void Filter_Screenshots_OnlyScreenshots()
    {
        RecomputeWithCuts(10);
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));
        _vm.ActiveFilter = NotesFilter.Screenshots;
        Assert.All(_vm.FilteredItems, i => Assert.Equal(TimelineItemKind.Screenshot, i.Kind));
        Assert.Single(_vm.FilteredItems);
    }

    [Fact]
    public void Filter_Segments_IncludesCutPoints()
    {
        RecomputeWithCuts(10, 20);
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));
        _vm.ActiveFilter = NotesFilter.Segments;
        // 片段筛选含裁切点：3 片段 + 2 裁切点
        Assert.Equal(5, _vm.FilteredItems.Count);
        Assert.DoesNotContain(_vm.FilteredItems, i => i.Kind == TimelineItemKind.Screenshot);
    }

    [Fact]
    public void Filter_OnlyMarked_OverlaysBaseFilter()
    {
        RecomputeWithCuts(10);
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));
        var shot = _vm.Items.OfType<ScreenshotListItem>().Single();
        var seg = _vm.Items.OfType<SegmentListItem>().First();
        // 勾选标记独立于列表选中：标记截图、选中片段（行选中不参与标记筛选）
        shot.IsMarked = true;
        seg.IsSelected = true;

        // 叠加开关开启：只看标记条目（基筛选 = 全部）
        _vm.OnlyMarked = true;
        var markedView = _vm.FilteredItems.ToList();
        Assert.Single(markedView);
        Assert.Same(shot, markedView[0]);

        // 与基筛选组合：截图基筛选 + 仅标记 → 仍只有标记的截图
        _vm.ActiveFilter = NotesFilter.Screenshots;
        Assert.Single(_vm.FilteredItems);
        Assert.Same(shot, _vm.FilteredItems[0]);

        // 与片段基筛选组合：无标记片段 → 空
        _vm.ActiveFilter = NotesFilter.Segments;
        Assert.Empty(_vm.FilteredItems);

        // 关闭叠加：恢复片段基筛选完整结果（2 片段 + 1 裁切点）
        _vm.OnlyMarked = false;
        Assert.Equal(3, _vm.FilteredItems.Count);
    }

    [Fact]
    public void Filter_OnlyMarked_RefreshesWhenMarkChanges()
    {
        RecomputeWithCuts(10);
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));
        _vm.OnlyMarked = true;
        Assert.Empty(_vm.FilteredItems);

        _vm.Items.OfType<ScreenshotListItem>().Single().IsMarked = true;

        Assert.Single(_vm.FilteredItems);
    }

    [Fact]
    public void Mark_PreservedAcrossRebuild()
    {
        RecomputeWithCuts(10);
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));
        _vm.Items.OfType<ScreenshotListItem>().Single().IsMarked = true;
        _vm.Items.OfType<SegmentListItem>().First().IsMarked = true;

        // 插入新裁切点触发 RebuildItems（片段行全部重建）
        _vm.RecomputeSegments(
            new[] { TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15) },
            TimeSpan.FromSeconds(30), null);

        var marked = _vm.GetMarkedItems();
        Assert.Equal(2, marked.Count);
        Assert.Contains(marked, i => i is ScreenshotListItem);
        Assert.Contains(marked, i => i is SegmentListItem);
    }

    [Fact]
    public void Filter_CutPointsOnly_OnlyCutPoints()
    {
        RecomputeWithCuts(10, 20);
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));
        _vm.ActiveFilter = NotesFilter.CutPointsOnly;
        Assert.Equal(2, _vm.FilteredItems.Count);
        Assert.All(_vm.FilteredItems, i => Assert.Equal(TimelineItemKind.CutPoint, i.Kind));
    }

    // --- 删裁切点 → 相邻片段合并 ---

    [Fact]
    public void RecomputeSegments_AfterCutPointRemoved_MergesAdjacentSegments()
    {
        RecomputeWithCuts(10, 20);
        Assert.Equal(3, _vm.Segments.Count);

        // 删除 10s 裁切点 → 0-10 与 10-20 合并为 0-20
        RecomputeWithCuts(20);

        Assert.Equal(2, _vm.Segments.Count);
        Assert.Equal(TimeSpan.Zero, _vm.Segments[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(20), _vm.Segments[0].End);
        // 片段重新编号
        var titles = _vm.Items.OfType<SegmentListItem>().Select(i => i.Title).ToList();
        Assert.Equal("片段 1 · 00:20.000", titles[0]);
    }

    // --- 时间轴标记：只来自截图 ---

    [Fact]
    public void Markers_FromScreenshotsOnly()
    {
        RecomputeWithCuts(10);
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(15)));

        Assert.Equal(2, _vm.Markers.Count);
        Assert.All(_vm.Markers, m => Assert.Equal(MarkerKind.Screenshot, m.Kind));
    }

    // --- 上/下一个跳转（基于统一列表） ---

    [Fact]
    public void JumpToNext_EmptyItems_NoSeek()
    {
        TimeSpan? seek = null;
        _vm.SeekRequested += (_, ts) => seek = ts;
        _vm.JumpToNext();
        Assert.Null(seek);
    }

    [Fact]
    public void JumpToNext_MovesToNextItemPosition()
    {
        RecomputeWithCuts(10);
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));
        _current = TimeSpan.FromSeconds(5); // 正好在截图上
        TimeSpan? seek = null;
        _vm.SeekRequested += (_, ts) => seek = ts;
        _vm.JumpToNext();
        Assert.Equal(TimeSpan.FromSeconds(10), seek); // 下一个条目是裁切点
    }

    [Fact]
    public void JumpToPrevious_MovesToPrevItemPosition()
    {
        RecomputeWithCuts(10);
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));
        _current = TimeSpan.FromSeconds(10); // 在裁切点上
        TimeSpan? seek = null;
        _vm.SeekRequested += (_, ts) => seek = ts;
        _vm.JumpToPrevious();
        Assert.Equal(TimeSpan.FromSeconds(5), seek);
    }

    [Fact]
    public void JumpToPrevious_AtFirstItem_NoSeek()
    {
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(10)));
        _current = TimeSpan.FromSeconds(5);
        TimeSpan? seek = null;
        _vm.SeekRequested += (_, ts) => seek = ts;
        _vm.JumpToPrevious();
        Assert.Null(seek);
    }

    // --- 标记导出 ---

    [Fact]
    public void GetSelectedItems_ReturnsSelectedAcrossKinds()
    {
        RecomputeWithCuts(10);
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));
        _vm.Items.OfType<ScreenshotListItem>().Single().IsSelected = true;
        _vm.Items.OfType<SegmentListItem>().First().IsSelected = true;

        var selected = _vm.GetSelectedItems();

        Assert.Equal(2, selected.Count);
        Assert.Contains(selected, i => i.Kind == TimelineItemKind.Screenshot);
        Assert.Contains(selected, i => i.Kind == TimelineItemKind.Segment);
    }

    [Fact]
    public void GetMarkedItems_IndependentFromSelection()
    {
        RecomputeWithCuts(10);
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));
        // 截图：仅标记；片段：仅行选中 —— 两种状态互不影响
        _vm.Items.OfType<ScreenshotListItem>().Single().IsMarked = true;
        _vm.Items.OfType<SegmentListItem>().First().IsSelected = true;

        var marked = _vm.GetMarkedItems();
        Assert.Single(marked);
        Assert.Contains(marked, i => i.Kind == TimelineItemKind.Screenshot);
        Assert.DoesNotContain(_vm.Items, i => i.IsMarked && i.IsSelected);
        Assert.Single(_vm.GetSelectedItems());
    }

    [Fact]
    public void ExportMarkedCommand_RaisesRequestEvent()
    {
        var raised = 0;
        _vm.ExportMarkedRequested += (_, _) => raised++;
        _vm.ExportMarkedCommand.Execute(null);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ExportDataCommand_RaisesRequestEvent()
    {
        var raised = 0;
        _vm.ExportDataRequested += (_, _) => raised++;
        _vm.ExportDataCommand.Execute(null);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void BuildExportData_ContainsCutPointsSegmentsScreenshotsAndMarks()
    {
        RecomputeWithCuts(10);
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));
        _vm.Items.OfType<ScreenshotListItem>().Single().IsMarked = true;
        _vm.Items.OfType<SegmentListItem>().First().IsMarked = true;

        var data = _vm.BuildExportData("C:\\video.mp4", TimeSpan.FromSeconds(30));

        Assert.Equal(1, data.Version);
        Assert.Equal("C:\\video.mp4", data.SourcePath);
        Assert.Equal("00:00:30.000", data.VideoLength);
        Assert.Single(data.CutPoints);
        Assert.Equal("00:00:10.000", data.CutPoints[0]);
        Assert.Equal(2, data.Segments.Count);
        Assert.True(data.Segments[0].Marked);   // 0-10s 段被标记
        Assert.False(data.Segments[1].Marked);
        Assert.Single(data.Screenshots);
        Assert.Equal("00:00:05.000", data.Screenshots[0].Position);
        Assert.True(data.Screenshots[0].Marked);
        Assert.NotEmpty(data.Screenshots[0].File);
        Assert.False(string.IsNullOrEmpty(data.ExportedAt));
    }

    // --- 删除截图 / 清空 ---

    [Fact]
    public void RemoveScreenshot_RemovesFromListAndMarkers()
    {
        var shot = MakeScreenshot(TimeSpan.FromSeconds(5));
        _vm.AddScreenshot(shot);
        _vm.RemoveScreenshot(shot);

        Assert.Empty(_vm.Items);
        Assert.Empty(_vm.Markers);
        Assert.Equal(0, _vm.ScreenshotCount);
    }

    [Fact]
    public void ClearAll_EmptiesEverything()
    {
        RecomputeWithCuts(10);
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));

        _vm.ClearAll();

        Assert.Empty(_vm.Items);
        Assert.Empty(_vm.Screenshots);
        Assert.Empty(_vm.Segments);
        Assert.Empty(_vm.Markers);
        Assert.Equal(0, _vm.ScreenshotCount);
        Assert.Equal(0, _vm.SegmentCount);
        Assert.Equal(0, _vm.CutPointCount);
    }

    [Fact]
    public void Changed_RaisedOnScreenshotAndRecompute()
    {
        var raised = 0;
        _vm.Changed += (_, _) => raised++;
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));
        RecomputeWithCuts(10);
        Assert.True(raised >= 2);
    }

    // --- 片段缩略图接线 ---

    [Fact]
    public async Task RecomputeSegments_WithThumbnailService_FillsSegmentThumbnails()
    {
        var thumbsDir = Path.Combine(_tempDir, "clips");
        var svc = new Services.SegmentThumbnailService(thumbsDir, (s, p, o) => Task.Run(() =>
        {
            File.WriteAllBytes(o, new byte[] { 1 });
            return o;
        }));
        _vm.Thumbnails = svc;

        RecomputeWithCuts(10);

        // 异步补抽完成后 ThumbnailPath 被填充
        var deadline = DateTime.Now.AddSeconds(5);
        while (_vm.Items.OfType<SegmentListItem>().Any(i => i.ThumbnailPath is null)
               && DateTime.Now < deadline)
            await Task.Delay(50);
        Assert.All(_vm.Items.OfType<SegmentListItem>(), i => Assert.NotNull(i.ThumbnailPath));
        // 孤儿缓存被清理后只剩当前段
        Assert.Equal(2, Directory.GetFiles(Path.Combine(thumbsDir, "thumbs")).Length);
    }
}
