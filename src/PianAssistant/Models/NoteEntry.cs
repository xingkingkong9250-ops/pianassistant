namespace PianAssistant.Models;

/// <summary>
/// A timestamped note attached to a position on the timeline.
/// </summary>
public sealed class NoteEntry
{
    public string SourcePath { get; }
    public TimeSpan Position { get; set; }
    public string Text { get; set; }
    public DateTime CreatedAt { get; }
    public DateTime? UpdatedAt { get; set; }

    public NoteEntry(string sourcePath, TimeSpan position, string text, DateTime? createdAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(text);
        SourcePath = sourcePath;
        Position = position;
        Text = text;
        CreatedAt = createdAt ?? DateTime.Now;
    }
}
