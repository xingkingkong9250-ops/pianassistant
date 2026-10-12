using System.IO;

namespace PianAssistant.Models;

/// <summary>
/// Top-level project that groups a playlist, clips, screenshots, notes.
/// M3+ for persistence; M0 only holds the in-memory shape.
/// </summary>
public sealed class ProjectFile
{
    public string Name { get; set; }
    public string? ProjectFilePath { get; set; }
    public List<MediaFile> Playlist { get; } = new();
    public List<ClipRange> Clips { get; } = new();
    public List<ScreenshotEntry> Screenshots { get; } = new();
    public List<NoteEntry> Notes { get; } = new();

    public ProjectFile(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string DefaultExportDirectory =>
        ProjectFilePath is { } path
            ? Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, Name + "_exports")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PianAssistant", Name + "_exports");
}
