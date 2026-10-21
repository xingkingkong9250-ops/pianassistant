using PianAssistant.Models;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>撤回历史服务测试（v1.6.0）：压栈/弹出/上限裁剪/清空/事件。</summary>
public class UndoHistoryServiceTests
{
    private static NotesSnapshot Snapshot(int cutMs) => new()
    {
        CutPoints = new List<TimeSpan> { TimeSpan.FromMilliseconds(cutMs) },
        Screenshots = new List<NotesSnapshotScreenshot>(),
        MarkedSegments = new HashSet<(long, long)>(),
        MarkedScreenshotPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        MarkedCutPointMs = new HashSet<long>(),
    };

    [Fact]
    public void PushPop_LifoOrder()
    {
        var undo = new UndoHistoryService(() => 5);
        undo.Push(Snapshot(100));
        undo.Push(Snapshot(200));
        Assert.Equal(2, undo.Count);
        Assert.True(undo.CanUndo);

        var top = undo.Pop();
        Assert.NotNull(top);
        Assert.Equal(TimeSpan.FromMilliseconds(200), top.CutPoints[0]);
        Assert.Equal(1, undo.Count);
    }

    [Fact]
    public void Pop_EmptyStack_ReturnsNull()
    {
        var undo = new UndoHistoryService(() => 5);
        Assert.False(undo.CanUndo);
        Assert.Null(undo.Pop());
    }

    [Fact]
    public void Push_ExceedsLimit_DropsOldest()
    {
        var undo = new UndoHistoryService(() => 3);
        for (var i = 1; i <= 5; i++)
            undo.Push(Snapshot(i * 100));

        Assert.Equal(3, undo.Count);
        // 最旧两份（100/200）被丢弃，栈顶为最新
        Assert.Equal(TimeSpan.FromMilliseconds(500), undo.Pop()!.CutPoints[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(400), undo.Pop()!.CutPoints[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(300), undo.Pop()!.CutPoints[0]);
        Assert.Null(undo.Pop());
    }

    [Fact]
    public void Push_LimitReadLiveFromSettings()
    {
        var limit = 5;
        var undo = new UndoHistoryService(() => limit);
        for (var i = 1; i <= 5; i++)
            undo.Push(Snapshot(i));
        Assert.Equal(5, undo.Count);

        // 设置调小后下一次压栈即按新上限裁剪
        limit = 2;
        undo.Push(Snapshot(6));
        Assert.Equal(2, undo.Count);
    }

    [Fact]
    public void Push_LimitClampedToAtLeastOne()
    {
        var undo = new UndoHistoryService(() => 0);
        undo.Push(Snapshot(1));
        Assert.Equal(1, undo.Count);
    }

    [Fact]
    public void Clear_EmptiesStack()
    {
        var undo = new UndoHistoryService(() => 5);
        undo.Push(Snapshot(1));
        undo.Push(Snapshot(2));
        undo.Clear();
        Assert.Equal(0, undo.Count);
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void Changed_RaisedOnPushPopClear()
    {
        var undo = new UndoHistoryService(() => 5);
        var raised = 0;
        undo.Changed += (_, _) => raised++;

        undo.Push(Snapshot(1));
        undo.Pop();
        undo.Push(Snapshot(2));
        undo.Clear();
        Assert.Equal(4, raised);

        // 空栈 Pop / Clear 不触发
        undo.Pop();
        undo.Clear();
        Assert.Equal(4, raised);
    }
}
