using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// Abstraction over the underlying video engine (LibVLC).
/// Used for stub-based testing without spinning up a real libvlc instance.
/// </summary>
public interface IVideoPlayback : IDisposable
{
    event EventHandler<TimeSpan>? PositionChanged;
    event EventHandler? EndReached;
    event EventHandler? Playing;
    event EventHandler? Paused;
    event EventHandler? Stopped;

    bool IsPlaying { get; }
    TimeSpan Position { get; }
    TimeSpan Length { get; }
    double Rate { get; set; }
    int Volume { get; set; }
    bool IsMuted { get; set; }
    double Fps { get; }
    int Width { get; }
    int Height { get; }
    string? CurrentPath { get; }

    void Open(string path);
    void Play();
    void Pause();
    void TogglePlayPause();
    void Stop();
    void NextFrame();
    void Seek(TimeSpan position);
    void SeekRelative(TimeSpan delta);
    void SeekPercent(double percent);
    void SetRate(double rate);
    string TakeSnapshot(string outputPath);
}

/// <summary>
/// Aggregated state event raised by the playback service.
/// </summary>
public sealed class PlaybackStateChangedEventArgs : EventArgs
{
    public TimeSpan Position { get; init; }
    public bool IsPlaying { get; init; }
    public double Rate { get; init; }
    public TimeSpan Length { get; init; }
}
