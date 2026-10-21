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
    /// <summary>加载外挂字幕文件（.srt/.ass/.vtt 等）到当前媒体。</summary>
    void LoadSubtitle(string path);

    /// <summary>轨道列表就绪/变化（开始播放、加载外挂字幕后触发）。</summary>
    event EventHandler? TracksChanged;

    /// <summary>当前媒体的音轨列表（未播放时为空）。</summary>
    IReadOnlyList<TrackInfo> GetAudioTracks();

    /// <summary>当前媒体的字幕轨列表（未播放时为空；不含「关闭字幕」项，由调用方自行追加）。</summary>
    IReadOnlyList<TrackInfo> GetSubtitleTracks();

    /// <summary>当前音轨 id（-1 表示无/禁用）。</summary>
    int CurrentAudioTrack { get; }

    /// <summary>当前字幕轨 id（-1 表示关闭）。</summary>
    int CurrentSubtitleTrack { get; }

    /// <summary>切换音轨。高倍速禁音轨策略期间仅记录选择，回落后生效。</summary>
    void SetAudioTrack(int id);

    /// <summary>切换字幕轨；-1 关闭字幕显示。</summary>
    void SetSubtitleTrack(int id);
}

/// <summary>音轨/字幕轨描述（LibVLC TrackDescription 的简化投影）。</summary>
public sealed record TrackInfo(int Id, string Name);

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
