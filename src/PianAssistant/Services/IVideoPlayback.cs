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
    /// <summary>
    /// 总时长就绪 / 变化（v1.7.7）。Media.Duration 只有在媒体解析完成后才被填充，
    /// Length 属性可能在 Open 后长时间返回 TimeSpan.Zero。订阅这个事件可以在
    /// 时长从 0 变为实际值时及时刷新 UI。
    /// </summary>
    event EventHandler? LengthChanged;

    bool IsPlaying { get; }
    TimeSpan Position { get; }
    /// <summary>
    /// 读取当前播放位置的精确值（直读播放引擎原生时钟，绕过节流缓存）。
    /// <see cref="Position"/> 由 TimeChanged 事件缓存驱动，高倍速时每次回调间隔
    /// 折算成媒体时间可达 1 秒以上，暂停瞬间则可能落后数帧；截图等对时间精度
    /// 敏感的场景必须用本方法（v1.15.7 精准截图修复）。
    /// </summary>
    TimeSpan GetPrecisePosition();
    TimeSpan Length { get; }
    /// <summary>
    /// 播放倍速。读取返回「期望倍速」：原生层的实际套用由后台控制线程异步完成
    /// （set_rate 会阻塞到 input 线程响应，不能在 UI 线程直接调用，v1.7.3）。
    /// </summary>
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
