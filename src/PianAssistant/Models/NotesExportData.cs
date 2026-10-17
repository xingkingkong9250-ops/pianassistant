using System.Text.Json.Serialization;

namespace PianAssistant.Models;

/// <summary>
/// 「导出数据」JSON 结构（W1-C2-D-02）：当前播放视频的裁切点、
/// 自动分段、截图清单与勾选标记状态的完整快照，便于归档或二次处理。
/// 时间码统一 HH:mm:ss.fff。
/// </summary>
public sealed class NotesExportData
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("sourcePath")]
    public string? SourcePath { get; set; }

    [JsonPropertyName("exportedAt")]
    public string? ExportedAt { get; set; }

    /// <summary>视频总时长（HH:mm:ss.fff；未知时为 null）。</summary>
    [JsonPropertyName("videoLength")]
    public string? VideoLength { get; set; }

    [JsonPropertyName("cutPoints")]
    public List<string> CutPoints { get; set; } = new();

    [JsonPropertyName("segments")]
    public List<ExportSegment> Segments { get; set; } = new();

    [JsonPropertyName("screenshots")]
    public List<ExportScreenshot> Screenshots { get; set; } = new();
}

/// <summary>片段导出条目：编号 + 起止时间码 + 勾选标记。</summary>
public sealed class ExportSegment
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("start")]
    public string Start { get; set; } = string.Empty;

    [JsonPropertyName("end")]
    public string End { get; set; } = string.Empty;

    [JsonPropertyName("marked")]
    public bool Marked { get; set; }
}

/// <summary>截图导出条目：位置 + 文件路径 + 勾选标记。</summary>
public sealed class ExportScreenshot
{
    [JsonPropertyName("position")]
    public string Position { get; set; } = string.Empty;

    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("marked")]
    public bool Marked { get; set; }
}
