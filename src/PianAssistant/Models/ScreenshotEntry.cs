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

    public ScreenshotEntry(string sourcePath, string outputPath, TimeSpan position, DateTime capturedAt, string? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        SourcePath = sourcePath;
        OutputPath = outputPath;
        Position = position;
        CapturedAt = capturedAt;
        Label = label;
    }
}
