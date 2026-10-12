namespace PianAssistant.Models;

/// <summary>
/// Metadata for a screenshot captured from the player.
/// </summary>
public sealed class ScreenshotEntry
{
    public string SourcePath { get; }
    public string OutputPath { get; }
    public TimeSpan Position { get; }
    public DateTime CapturedAt { get; }
    public string? Label { get; set; }

    /// <summary>数据库行 ID；新建截图时为 null，从数据库加载时赋值。</summary>
    public string? Id { get; }

    public ScreenshotEntry(string sourcePath, string outputPath, TimeSpan position, DateTime capturedAt, string? label = null, string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        SourcePath = sourcePath;
        OutputPath = outputPath;
        Position = position;
        CapturedAt = capturedAt;
        Label = label;
        Id = id;
    }
}
