using System.IO;
using PianAssistant.Services;

namespace PianAssistant.Tests.Stubs;

/// <summary>
/// Stub implementation of IVideoPlayback for unit testing.
/// No real LibVLC dependency; tracks state in memory.
/// </summary>
public sealed class StubVideoPlayback : IVideoPlayback
{
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? EndReached;
    public event EventHandler? Playing;
    public event EventHandler? Paused;
    public event EventHandler? Stopped;

    public bool IsPlaying { get; private set; }
    public TimeSpan Position { get; private set; }
    public TimeSpan Length { get; set; } = TimeSpan.FromMinutes(5);
    public double Rate { get; set; } = 1.0;
    public double Fps { get; set; } = 25.0;
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public string? CurrentPath { get; private set; }
    public string? LastSnapshotPath { get; private set; }
    public bool SnapshotShouldFail { get; set; }

    public void Open(string path)
    {
        CurrentPath = path;
        Position = TimeSpan.Zero;
    }

    public void Play()
    {
        IsPlaying = true;
        Playing?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        IsPlaying = false;
        Paused?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePlayPause()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    public void Stop()
    {
        IsPlaying = false;
        Stopped?.Invoke(this, EventArgs.Empty);
    }

    public void NextFrame() { }

    public void Seek(TimeSpan position)
    {
        Position = position;
        PositionChanged?.Invoke(this, Position);
    }

    public void SeekRelative(TimeSpan delta)
    {
        Position += delta;
        if (Position < TimeSpan.Zero) Position = TimeSpan.Zero;
        PositionChanged?.Invoke(this, Position);
    }

    public void SeekPercent(double percent)
    {
        Position = TimeSpan.FromMilliseconds(Length.TotalMilliseconds * percent);
        PositionChanged?.Invoke(this, Position);
    }

    public void SetRate(double rate) => Rate = rate;

    public string TakeSnapshot(string outputPath)
    {
        if (SnapshotShouldFail)
            throw new InvalidOperationException("Stub snapshot forced to fail.");
        LastSnapshotPath = outputPath;
        // Create a dummy file so MakeUnique logic doesn't kick in.
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(outputPath, "stub");
        return outputPath;
    }

    /// <summary>Simulate playback advancing the position.</summary>
    public void SimulatePositionChange(TimeSpan newPos)
    {
        Position = newPos;
        PositionChanged?.Invoke(this, Position);
    }

    /// <summary>Simulate end of media.</summary>
    public void SimulateEndReached() => EndReached?.Invoke(this, EventArgs.Empty);

    public void Dispose() { }
}
