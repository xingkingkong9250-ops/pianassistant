using LibVLCSharp.Shared;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PianAssistant.Services;

/// <summary>
/// LibVLC-backed video playback service.
/// Wraps <see cref="LibVLCSharp.MediaPlayer"/> to expose the
/// <see cref="IVideoPlayback"/> contract for VM binding and testing.
/// </summary>
public sealed class VideoPlaybackService : IVideoPlayback
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private Media? _media;
    private string? _currentPath;
    private bool _disposed;

    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? EndReached;
    public event EventHandler? Playing;
    public event EventHandler? Paused;
    public event EventHandler? Stopped;

    public bool IsPlaying => _mediaPlayer.IsPlaying;
    public TimeSpan Position
    {
        get
        {
            var media = _mediaPlayer.Media;
            if (media is null || media.Duration <= 0) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds(media.Duration * _mediaPlayer.Position);
        }
    }
    public TimeSpan Length =>
        _media?.Duration > 0 ? TimeSpan.FromMilliseconds(_media.Duration) : TimeSpan.Zero;
    public double Rate
    {
        get => _mediaPlayer.Rate;
        set => SetRate(value);
    }
    public int Volume
    {
        get => _mediaPlayer.Volume;
        set => _mediaPlayer.Volume = Math.Clamp(value, 0, 100);
    }
    public bool IsMuted
    {
        get => _mediaPlayer.Mute;
        set => _mediaPlayer.Mute = value;
    }
    public double Fps { get; private set; } = 25.0;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public string? CurrentPath => _currentPath;

    /// <summary>
    /// Exposes the underlying MediaPlayer for the LibVLCSharp.WPF VideoView
    /// to attach (HostWindow / MediaPlayer property binding).
    /// </summary>
    public MediaPlayer LibVlcMediaPlayer => _mediaPlayer;

    public VideoPlaybackService()
    {
        _libVlc = new LibVLC();
        _mediaPlayer = new MediaPlayer(_libVlc)
        {
            EnableHardwareDecoding = true,
            Scale = 0f, // fit
        };
        // LibVLC 事件从原生线程回调；统一 marshal 到 UI 线程，避免
        // ViewModel→集合→WPF 渲染链在非 UI 线程执行引发跨线程崩溃（v1.1.2）
        _mediaPlayer.TimeChanged += (_, e) => RaiseOnUi(() =>
        {
            // 用事件携带的精确毫秒而非 Position 比例换算：seek 后内部
            // Position 比例可能尚未更新，比例换算会回报旧位置（回弹源之一）
            var timeMs = e.Time;
            var now = DateTime.UtcNow;
            if (IsStaleSeekReport(timeMs, _pendingSeekTargetMs,
                    (now - _pendingSeekAtUtc).TotalMilliseconds))
                return; // seek 未到位的旧位置帧：抑制上报，避免播放头回弹
            _pendingSeekTargetMs = -1; // 到位或超窗，恢复正常上报
            PositionChanged?.Invoke(this, TimeSpan.FromMilliseconds(timeMs));
        });
        _mediaPlayer.EndReached += (_, _) => RaiseOnUi(() =>
        {
            _pendingSeekTargetMs = -1;
            EndReached?.Invoke(this, EventArgs.Empty);
        });
        // Parse track info (fps/dimensions) once playback starts — tracks
        // are demuxed by then. Replaces ESAdded (not available in 3.8.2).
        _mediaPlayer.Playing += (_, _) => RaiseOnUi(() =>
        {
            OnMediaParsed();
            // 音轨此刻才完成选择：若当前处于高速档（如打开时按倍速记忆恢复 8×），
            // 需再次应用音轨策略禁用音频，解除 4× 时钟钳制
            ApplyHighRateAudioPolicy(Rate);
            Playing?.Invoke(this, EventArgs.Empty);
        });
        _mediaPlayer.Paused += (_, _) => RaiseOnUi(() => Paused?.Invoke(this, EventArgs.Empty));
        _mediaPlayer.Stopped += (_, _) => RaiseOnUi(() =>
        {
            _pendingSeekTargetMs = -1;
            Stopped?.Invoke(this, EventArgs.Empty);
        });
    }

    // --- seek 回弹抑制（v1.2.2）---

    // LibVLC 的 Time 赋值异步生效：赋值后 demux/解码管线仍会输出若干旧位置帧，
    // TimeChanged 随之回报旧时间 → UI 播放头「回弹」（倍速下管线缓冲更大，更明显）。
    // seek 后进入短抑制窗：窗内偏离目标超过容差的旧位置报告一律丢弃，
    // 位置接近目标（到位）或超窗后恢复正常上报。
    private const double SeekSettleWindowMs = 400;
    private const long SeekSettleToleranceMs = 600;
    private long _pendingSeekTargetMs = -1;
    private DateTime _pendingSeekAtUtc;

    /// <summary>
    /// 判定 TimeChanged 报告是否为 seek 未到位的旧位置帧（应丢弃）。纯函数便于单测。
    /// </summary>
    public static bool IsStaleSeekReport(
        long timeMs, long pendingTargetMs, double elapsedMs,
        double windowMs = SeekSettleWindowMs, long toleranceMs = SeekSettleToleranceMs)
        => pendingTargetMs >= 0
           && elapsedMs <= windowMs
           && Math.Abs(timeMs - pendingTargetMs) > toleranceMs;

    /// <summary>在 UI 线程执行（VLC 原生线程回调一律经此调度；无 Application 时同步执行，测试环境安全）。</summary>
    private static void RaiseOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }

    private void OnMediaParsed()
    {
        // M0: dimensions/fps remain at defaults (Width=0, Height=0, Fps=25.0).
        // Track-data API surface differs across LibVLCSharp minor versions;
        // will refine in M2+ once the exact property names are confirmed.
        try
        {
            // Placeholder for future track metadata parsing.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VideoPlayback] OnMediaParsed failed: {ex.Message}");
        }
    }

    public void Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) throw new FileNotFoundException("Video file not found.", path);
        _currentPath = path;
        _media = new Media(_libVlc, new Uri(path));
        _mediaPlayer.Media = _media;
        // 新素材音轨 id 无效化：高速档禁用的音轨记忆仅在同一素材内有效
        _savedAudioTrack = null;
    }

    public void Play() => _mediaPlayer.Play();
    public void Pause() => _mediaPlayer.SetPause(true);
    public void TogglePlayPause()
    {
        if (_mediaPlayer.IsPlaying) _mediaPlayer.SetPause(true);
        else _mediaPlayer.Play();
    }
    public void Stop() => _mediaPlayer.Stop();
    public void NextFrame() => _mediaPlayer.NextFrame();

    public void Seek(TimeSpan position)
    {
        var lengthMs = _media?.Duration ?? 0;
        if (lengthMs <= 0) return;
        SeekToMs(Math.Clamp((long)position.TotalMilliseconds, 0, lengthMs));
    }

    public void SeekRelative(TimeSpan delta)
    {
        if (Fps <= 0) return;
        // 连续触发（长按）时 Time 属性可能仍是上次 seek 前的旧值（赋值异步生效），
        // 抑制窗内以上次 seek 目标为基准叠加，避免偏差累积与来回跳动。
        var now = DateTime.UtcNow;
        var baseMs = _pendingSeekTargetMs >= 0
                     && (now - _pendingSeekAtUtc).TotalMilliseconds <= SeekSettleWindowMs
            ? _pendingSeekTargetMs
            : _mediaPlayer.Time;
        var targetMs = baseMs + (long)delta.TotalMilliseconds;
        if (targetMs < 0) targetMs = 0;
        var lengthMs = _media?.Duration ?? long.MaxValue;
        if (targetMs > lengthMs) targetMs = lengthMs;
        SeekToMs(targetMs);
    }

    public void SeekPercent(double percent)
    {
        if (_media is null) return;
        var lengthMs = _media.Duration;
        if (lengthMs <= 0) return;
        SeekToMs(Math.Clamp((long)(Math.Clamp(percent, 0, 1) * lengthMs), 0, lengthMs));
    }

    /// <summary>
    /// 统一 seek 入口：用 <see cref="MediaPlayer.Time"/>（毫秒）而非 <see cref="MediaPlayer.Position"/>
    /// —— LibVLC 3 暂停态下 Position 赋值不刷新画面且不触发 TimeChanged，
    /// 而 Time 赋值（逐帧路径 SeekRelative 所用）可靠。暂停时补一帧渲染并手动通知 UI。
    /// </summary>
    private void SeekToMs(long targetMs)
    {
        var wasPlaying = _mediaPlayer.IsPlaying;
        _pendingSeekTargetMs = targetMs;
        _pendingSeekAtUtc = DateTime.UtcNow;
        _mediaPlayer.Time = targetMs;
        if (!wasPlaying)
        {
            // 暂停态：步进一帧强制渲染新位置画面（步进量 ≈1 帧，误差可忽略）；
            // TimeChanged 暂停态不触发，手动通知播放头立即更新。
            _mediaPlayer.NextFrame();
            PositionChanged?.Invoke(this, TimeSpan.FromMilliseconds(targetMs));
        }
    }

    /// <summary>
    /// 音频时钟会把有效倍速钳制在约 4×（LibVLC 3 aout 限制）。
    /// 高于该阈值时禁用音轨（同步静音、解除钳制），回落时恢复原音轨。
    /// </summary>
    private const double HighRateAudioLimit = 4.0;
    private int? _savedAudioTrack;

    public void SetRate(double rate)
    {
        // 服务层兜底 0.25~32（VM 层已按设置的最高倍率钳制，勿在此收紧）
        var clamped = Math.Clamp(rate, 0.25, 32.0);
        ApplyHighRateAudioPolicy(clamped);
        _mediaPlayer.SetRate((float)clamped);
    }

    /// <summary>按倍速应用音轨策略：&gt;4× 禁用音轨真实加速，≤4× 恢复音轨。</summary>
    private void ApplyHighRateAudioPolicy(double rate)
    {
        if (rate > HighRateAudioLimit)
        {
            var current = _mediaPlayer.AudioTrack;
            if (current != -1)
            {
                if (current >= 0) _savedAudioTrack = current;
                _mediaPlayer.SetAudioTrack(-1);
                _mediaPlayer.SetRate((float)rate); // 音轨禁用后重新应用，确保解除音频时钟钳制
            }
        }
        else if (_savedAudioTrack is int track)
        {
            _mediaPlayer.SetAudioTrack(track);
            _savedAudioTrack = null;
            _mediaPlayer.SetRate((float)rate);
        }
    }

    /// <summary>
    /// 加载外挂字幕文件。LibVLCSharp 3.8.2 通过 MediaPlayer.AddSlave
    /// 以 Subtitle 类型挂载外挂字幕轨。
    /// </summary>
    public void LoadSubtitle(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Subtitle file not found.", path);
        var uri = new Uri(path).AbsoluteUri;
        _mediaPlayer.AddSlave(MediaSlaveType.Subtitle, uri, true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _mediaPlayer.Stop(); } catch { }
        _mediaPlayer.Dispose();
        _media?.Dispose();
        _libVlc.Dispose();
    }
}
