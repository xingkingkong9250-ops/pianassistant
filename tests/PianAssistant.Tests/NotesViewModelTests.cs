using System.IO;
using PianAssistant.Models;
using PianAssistant.ViewModels;
using Xunit;

namespace PianAssistant.Tests;

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

    // --- 笔记模板 ---

    [Fact]
    public void Templates_ContainsNoneAndSixPresets()
    {
        Assert.Equal(6, NotesViewModel.Templates.Count);
        Assert.Equal(7, _vm.TemplateOptions.Count); // 无模板 + 6 预设
        Assert.Equal("无模板", _vm.TemplateOptions[0].Name);
    }

    [Fact]
    public void AddNote_WithoutTemplate_UsesRawText()
    {
        _vm.NewNoteText = "运镜稳定";
        _vm.AddNote(null);
        Assert.Equal("运镜稳定", _vm.Activities[0].Label);
    }

    [Fact]
    public void AddNote_WithTemplate_PrependsPrefix()
    {
        _vm.ActiveTemplate = NotesViewModel.Templates[0]; // 构图
        _vm.NewNoteText = "三分法构图";
        _vm.AddNote(null);
        Assert.Equal("[构图] 三分法构图", _vm.Activities[0].Label);
        Assert.Equal(string.Empty, _vm.NewNoteText); // 添加后清空输入
    }

    // --- 笔记重命名/删除 ---

    [Fact]
    public void RenameNote_UpdatesLabelAndExitsEditing()
    {
        _vm.NewNoteText = "旧文本";
        _vm.AddNote(null);
        var entry = _vm.Activities[0];
        entry.IsEditing = true;

        _vm.RenameNote(entry, "  新文本  ");

        Assert.Equal("新文本", entry.Label);
        Assert.False(entry.IsEditing);
    }

    [Fact]
    public void RenameNote_EmptyText_Ignored()
    {
        _vm.NewNoteText = "保留原文";
        _vm.AddNote(null);
        var entry = _vm.Activities[0];

        _vm.RenameNote(entry, "   ");

        Assert.Equal("保留原文", entry.Label);
    }

    [Fact]
    public void Remove_RemovesEntryFromActivities()
    {
        _vm.NewNoteText = "待删除";
        _vm.AddNote(null);
        var entry = _vm.Activities[0];

        _vm.Remove(entry);

        Assert.DoesNotContain(entry, _vm.Activities);
    }

    // --- 搜索/时间范围过滤 ---

    [Fact]
    public void SearchFilter_MatchesLabelCaseInsensitive()
    {
        _vm.NewNoteText = "Camera Move";
        _vm.AddNote(null);
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));

        _vm.SearchText = "camera";
        Assert.Single(_vm.FilteredActivities);
        Assert.Equal("Camera Move", _vm.FilteredActivities[0].Label);

        _vm.SearchText = string.Empty;
        Assert.Equal(2, _vm.FilteredActivities.Count);
    }

    [Fact]
    public void SearchFilter_ActivityChange_RefreshesFiltered()
    {
        _vm.SearchText = "截图";
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));
        Assert.Single(_vm.FilteredActivities);
    }

    [Fact]
    public void TimeRangeFilter_FiltersByPosition()
    {
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(30)));
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(90)));

        _vm.FilterStart = TimeSpan.FromSeconds(10);
        _vm.FilterEnd = TimeSpan.FromSeconds(60);

        var item = Assert.Single(_vm.FilteredActivities);
        Assert.Equal(TimeSpan.FromSeconds(30), item.Position);
    }

    // --- 时间轴标记 ---

    [Fact]
    public void Markers_ExcludeCutPointsAndFollowActivities()
    {
        _vm.NewNoteText = "笔记";
        _vm.AddNote(null);
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(5)));
        _vm.AddClip(new ClipRange("video.mp4", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)), "out.mp4");
        _vm.AddCutPoint(TimeSpan.FromSeconds(30));

        // 笔记+截图+片段共 3 个标记；裁切点由时间轴红三角表示，不生成
        Assert.Equal(3, _vm.Markers.Count);

        _vm.ClearAll();
        Assert.Empty(_vm.Markers);
    }

    // --- 上/下一个跳转（边界：无条目/首尾） ---

    [Fact]
    public void JumpToNext_EmptyActivities_NoSeek()
    {
        TimeSpan? seek = null;
        _vm.SeekRequested += (_, ts) => seek = ts;
        _vm.JumpToNext();
        Assert.Null(seek);
    }

    [Fact]
    public void JumpToNext_AtLastEntry_NoSeek()
    {
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(10)));
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(20)));
        _current = TimeSpan.FromSeconds(30);
        TimeSpan? seek = null;
        _vm.SeekRequested += (_, ts) => seek = ts;
        _vm.JumpToNext();
        Assert.Null(seek);
    }

    [Fact]
    public void JumpToNext_MovesToNextEntry()
    {
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(10)));
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(20)));
        _current = TimeSpan.FromSeconds(10); // 正好在第一条上
        TimeSpan? seek = null;
        _vm.SeekRequested += (_, ts) => seek = ts;
        _vm.JumpToNext();
        Assert.Equal(TimeSpan.FromSeconds(20), seek);
    }

    [Fact]
    public void JumpToPrevious_EmptyActivities_NoSeek()
    {
        TimeSpan? seek = null;
        _vm.SeekRequested += (_, ts) => seek = ts;
        _vm.JumpToPrevious();
        Assert.Null(seek);
    }

    [Fact]
    public void JumpToPrevious_AtFirstEntry_NoSeek()
    {
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(10)));
        _current = TimeSpan.FromSeconds(5);
        TimeSpan? seek = null;
        _vm.SeekRequested += (_, ts) => seek = ts;
        _vm.JumpToPrevious();
        Assert.Null(seek);
    }

    [Fact]
    public void JumpToPrevious_MovesToPrevEntry()
    {
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(10)));
        _vm.AddScreenshot(MakeScreenshot(TimeSpan.FromSeconds(20)));
        _current = TimeSpan.FromSeconds(20);
        TimeSpan? seek = null;
        _vm.SeekRequested += (_, ts) => seek = ts;
        _vm.JumpToPrevious();
        Assert.Equal(TimeSpan.FromSeconds(10), seek);
    }
}
