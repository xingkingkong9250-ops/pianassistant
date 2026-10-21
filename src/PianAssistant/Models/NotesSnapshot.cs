namespace PianAssistant.Models;

/// <summary>
/// 笔记数据快照（撤回功能，v1.6.0）：一次笔记操作发生前的完整可恢复状态，
/// 含裁切点、截图清单与全部勾选标记（片段/截图/裁切点）。
/// 快照为纯数据，Undo 时整体覆盖回当前会话。
/// </summary>
public sealed class NotesSnapshot
{
    /// <summary>裁切点清单（升序）。</summary>
    public required List<TimeSpan> CutPoints { get; init; }

    /// <summary>截图清单（按时间升序）。</summary>
    public required List<NotesSnapshotScreenshot> Screenshots { get; init; }

    /// <summary>已勾选标记的片段（按起止 Ticks 识别）。</summary>
    public required HashSet<(long StartTicks, long EndTicks)> MarkedSegments { get; init; }

    /// <summary>已勾选标记的截图（按输出路径识别，忽略大小写）。</summary>
    public required HashSet<string> MarkedScreenshotPaths { get; init; }

    /// <summary>已勾选标记的裁切点（按毫秒位置识别）。</summary>
    public required HashSet<long> MarkedCutPointMs { get; init; }
}

/// <summary>快照中的单张截图记录（与 <see cref="ScreenshotEntry"/> 字段对齐）。</summary>
public sealed class NotesSnapshotScreenshot
{
    public required string SourcePath { get; init; }
    public required string OutputPath { get; init; }
    public required TimeSpan Position { get; init; }
    public required DateTime CapturedAt { get; init; }
    public string? Label { get; init; }
}
