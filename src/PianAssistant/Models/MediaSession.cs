using System.Collections.Generic;

namespace PianAssistant.Models;

/// <summary>
/// 单个素材的拉片会话（M7 自动保存/恢复）。
/// 主键为快速键（路径+大小+mtime）；<see cref="ContentHash"/> 为可选的内容 SHA-256 规范键，
/// 在后台算完后回写。持久化对应 <c>media_sessions</c> 表及明细表。
/// </summary>
public sealed class MediaSession
{
    public string FastKey { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public long MtimeUtcTicks { get; set; }
    public long LastPositionMs { get; set; }
    public double Rate { get; set; } = 1.0;
    public long? InPointMs { get; set; }
    public long? OutPointMs { get; set; }
    public System.DateTime UpdatedAt { get; set; } = System.DateTime.Now;
    public string? ProjectName { get; set; }

    /// <summary>裁切点（毫秒）。</summary>
    public List<long> CutPointsMs { get; } = new();

    /// <summary>截图清单。</summary>
    public List<SessionScreenshot> Screenshots { get; } = new();

    /// <summary>时间戳笔记。</summary>
    public List<SessionNote> Notes { get; } = new();
}

/// <summary>会话内的一条截图元数据。</summary>
public sealed class SessionScreenshot
{
    public string FilePath { get; set; } = string.Empty;
    public long PositionMs { get; set; }
    public System.DateTime CapturedAt { get; set; }
    public string? Label { get; set; }
}

/// <summary>会话内的一条笔记。</summary>
public sealed class SessionNote
{
    public long PositionMs { get; set; }
    public string Text { get; set; } = string.Empty;
    public System.DateTime CreatedAt { get; set; }
}

/// <summary>
/// 采集自当前会话的内存快照（由 ShellViewModel 提供），
/// 供 <see cref="Services.MediaSessionService"/> 落库；服务层不直接依赖 ViewModel。
/// </summary>
public sealed class MediaSessionSnapshot
{
    public string Path { get; set; } = string.Empty;
    public System.TimeSpan Position { get; set; }
    public double Rate { get; set; } = 1.0;
    public System.TimeSpan? InPoint { get; set; }
    public System.TimeSpan? OutPoint { get; set; }
    public IReadOnlyList<System.TimeSpan> CutPoints { get; set; } = System.Array.Empty<System.TimeSpan>();
    public IReadOnlyList<ScreenshotEntry> Screenshots { get; set; } = System.Array.Empty<ScreenshotEntry>();
    public IReadOnlyList<NoteEntry> Notes { get; set; } = System.Array.Empty<NoteEntry>();
}
