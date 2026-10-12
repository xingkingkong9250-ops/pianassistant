namespace PianAssistant.Models;

/// <summary>
/// An in/out point range on a video timeline.
/// </summary>
public sealed class ClipRange
{
    public string SourcePath { get; }
    public TimeSpan InPoint { get; set; }
    public TimeSpan? OutPoint { get; set; }
    public string? Label { get; set; }

    public ClipRange(string sourcePath, TimeSpan inPoint, TimeSpan? outPoint = null, string? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        SourcePath = sourcePath;
        InPoint = inPoint;
        OutPoint = outPoint;
        Label = label;
    }

    /// <summary>Duration if both points are set; null otherwise.</summary>
    public TimeSpan? Duration =>
        OutPoint is { } outPoint ? outPoint - InPoint : null;

    /// <summary>True when both in and out are set and out is strictly after in.</summary>
    public bool IsComplete =>
        OutPoint is { } outPoint && outPoint > InPoint;
}
