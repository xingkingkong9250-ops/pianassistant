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
    public event EventHandler? LengthChanged;

    public bool IsPlaying { get; private set; }
    public TimeSpan Position { get; private set; }
    public TimeSpan Length { get; set; } = TimeSpan.FromMinutes(5);
    public double Rate { get; set; } = 1.0;
    public int Volume { get; set; } = 100;
    public bool IsMuted { get; set; }
    public double Fps { get; set; } = 25.0;
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public string? CurrentPath { get; private set; }

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

    /// <summary>Stub: 记录最后一次加载的字幕路径。</summary>
    public string? LastSubtitlePath { get; private set; }

    public void LoadSubtitle(string path)
    {
        LastSubtitlePath = path;
    }

    // --- 轨道枚举与切换（v1.5.0 stub）---

    public event EventHandler? TracksChanged;

    /// <summary>Stub: 可注入的音轨列表。</summary>
    public List<TrackInfo> AudioTracks { get; } = new();

    /// <summary>Stub: 可注入的字幕轨列表。</summary>
    public List<TrackInfo> SubtitleTracks { get; } = new();

    public int CurrentAudioTrack { get; private set; } = -1;
    public int CurrentSubtitleTrack { get; private set; } = -1;

    public IReadOnlyList<TrackInfo> GetAudioTracks() => AudioTracks;
    public IReadOnlyList<TrackInfo> GetSubtitleTracks() => SubtitleTracks;

    public void SetAudioTrack(int id) => CurrentAudioTrack = id;
    public void SetSubtitleTrack(int id) => CurrentSubtitleTrack = id;

    /// <summary>测试辅助：触发 TracksChanged 事件。</summary>
    public void RaiseTracksChanged() => TracksChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>测试辅助：模拟 Media.Duration 就绪，触发 LengthChanged。</summary>
    public void RaiseLengthChanged() => LengthChanged?.Invoke(this, EventArgs.Empty);

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
