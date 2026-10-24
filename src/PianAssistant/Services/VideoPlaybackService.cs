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

    // --- 倍速/音轨/暂停串行化（v1.7.3）---
    // libvlc 的 set_rate / set_audio_track / get_audio_track / set_pause 都会通过
    // input 线程同步下发，高倍速反复重缓冲时 input 线程繁忙，这些调用会在调用线程
    // 上长时间阻塞。原先它们直接在 UI 线程（含 Playing 事件回调）执行，切换倍速时
    // 会把 UI 线程卡死（Windows Application Hang / AppHangB1）。
    // 现在统一投递到单一后台控制线程串行执行，并按「最后一次请求生效」合并，
    // UI 线程只写期望值，永不做阻塞的原生调用。
    private readonly object _controlSync = new();
    private readonly AutoResetEvent _controlSignal = new(false);
    private readonly Thread _controlThread;
    private double _desiredRate = 1.0;
    private bool _rateApplyPending;
    private bool _policyApplyPending;
    private bool _resetPolicyPending;
    private bool _pauseApplyPending;
    private bool _pendingPause;
    private bool _hasPendingAudioTrack;
    private int _pendingAudioTrack;
    private bool _stopControl;
    // 连续 seek 合并（v1.7.4）：pending seek 目标与是否已投递
    private long _seekTargetMs = -1;
    private bool _seekApplyPending;
    // 起播前无法 seek（input 未创建）：延迟到 Playing 时补发（会话恢复位置场景）
    private long _deferredSeekMs = -1;
    // 以下状态仅由后台控制线程读写（受 _controlSync 保护的访问点见各方法）
    private int? _savedAudioTrack;
    private bool _audioDisabledForHighRate;
    private bool _rateEstablished;
    // 当前媒体是否已上报过轨道列表：Playing 在高速档会频繁重发，不能每次都枚举轨道
    private bool _tracksRaisedForMedia;

    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? EndReached;
    public event EventHandler? Playing;
    public event EventHandler? Paused;
    public event EventHandler? Stopped;
    public event EventHandler? TracksChanged;

    public bool IsPlaying => _mediaPlayer.IsPlaying;
    public TimeSpan Position =>
        // 直读 VLC 内部精确毫秒（与 TimeChanged 上报同源）。原实现用
        // Position 浮点比例 × Duration 换算，长视频下有舍入误差且
        // seek 后内部比例可能滞后（v1.2.3 起与截图/播放头共用同一时间源）
        TimeSpan.FromMilliseconds(Math.Max(0, _mediaPlayer.Time));
    public TimeSpan Length =>
        _media?.Duration > 0 ? TimeSpan.FromMilliseconds(_media.Duration) : TimeSpan.Zero;
    public double Rate
    {
        // 返回「期望倍速」而非直读 VLC：libvlc_media_player_get_rate 会经 input
        // 线程同步取值，高倍速重缓冲时可能在 UI 线程阻塞（v1.7.3）
        get => DesiredRate;
        set => SetRate(value);
    }

    /// <summary>当前期望倍速（原生层由后台控制线程异步套用）。</summary>
    private double DesiredRate
    {
        get { lock (_controlSync) return _desiredRate; }
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
            // 需再次应用音轨策略禁用音频，解除 4× 时钟钳制。
            // 原生调用下沉到后台控制线程，避免 UI 线程阻塞（v1.7.3）
            RequestAudioPolicy();
            // 起播前的 seek（会话恢复位置）延迟到此刻补发：input 已创建，Time 赋值生效（v1.7.4）
            long deferred;
            lock (_controlSync)
            {
                deferred = _deferredSeekMs;
                _deferredSeekMs = -1;
                if (deferred >= 0)
                {
                    _seekTargetMs = deferred;
                    _seekApplyPending = true;
                }
            }
            if (deferred >= 0) _controlSignal.Set();
            // 播放开始后 demux 完成，音轨/字幕轨列表可用。
            // 仅每份媒体首次 Playing 上报：高速档重缓冲会反复触发 Playing，
            // 每次都枚举轨道（原生调用）会拖住 UI 线程（v1.7.3）
            bool raiseTracks;
            lock (_controlSync)
            {
                raiseTracks = !_tracksRaisedForMedia;
                _tracksRaisedForMedia = true;
            }
            if (raiseTracks) TracksChanged?.Invoke(this, EventArgs.Empty);
            Playing?.Invoke(this, EventArgs.Empty);
        });
        _mediaPlayer.Paused += (_, _) => RaiseOnUi(() => Paused?.Invoke(this, EventArgs.Empty));
        _mediaPlayer.Stopped += (_, _) => RaiseOnUi(() =>
        {
            _pendingSeekTargetMs = -1;
            lock (_controlSync) _rateEstablished = false;
            Stopped?.Invoke(this, EventArgs.Empty);
        });

        // 后台控制线程：唯一执行倍速/音轨/暂停等阻塞性原生调用的线程
        _controlThread = new Thread(ControlLoop)
        {
            IsBackground = true,
            Name = "PianAssistant.VlcControl",
        };
        _controlThread.Start();
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
    // 连续 seek 合并（v1.7.4）：UI 线程只更新期望位置，原生赋值由后台控制线程
    // 按「最后一次生效」执行；快进/后退风暴时不会被 input 线程拖死

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
        // 新素材音轨 id 无效化：高速档禁用的音轨记忆仅在同一素材内有效；
        // 策略状态由后台控制线程重置，避免与正在执行的倍速/音轨调用竞争
        lock (_controlSync)
        {
            _resetPolicyPending = true;
            _rateEstablished = false;
            _tracksRaisedForMedia = false;
            _deferredSeekMs = -1;
        }
        _controlSignal.Set();
    }

    public void Play()
    {
        lock (_controlSync)
        {
            _rateEstablished = false;
            // 播放队列优先级最高：立即唤醒，不等 seek 合并
            _pauseApplyPending = true;
            _pendingPause = false;
        }
        _controlSignal.Set();
    }

    /// <summary>暂停：同样下沉到后台控制线程，避免 set_pause 阻塞 UI（v1.7.3）。</summary>
    public void Pause()
    {
        lock (_controlSync)
        {
            _pendingPause = true;
            _pauseApplyPending = true;
        }
        _controlSignal.Set();
    }

    /// <summary>
    /// 播放/暂停切换。
    /// <para>
    /// 不能用 <see cref="MediaPlayer.IsPlaying"/> 判定当前是否在播放：
    /// LibVLC 的 is_playing 仅在 Playing 态返回 true，Buffering / Opening 期间
    /// 返回 false。高倍速播放时解码管线频繁重缓冲，此时按空格会被误判为
    /// 「未播放」→ 再次调用 Play()（空操作），表现为倍速下空格无法暂停
    /// （v1.7.2 修复）。改为按 VLC 状态机判定。
    /// </para>
    /// <para>
    /// 状态读取用缓存状态（非阻塞），真正的 SetPause/Play 交给后台控制线程：
    /// set_pause 同样要经 input 线程同步，高倍速重缓冲时在 UI 线程调用会卡死窗口（v1.7.3）。
    /// </para>
    /// </summary>
    public void TogglePlayPause()
    {
        var pause = IsPlaybackActive(_mediaPlayer.State);
        lock (_controlSync)
        {
            _pendingPause = pause;
            _pauseApplyPending = true;
        }
        _controlSignal.Set();
    }

    /// <summary>
    /// 状态机判定：当前是否处于「在播放（含重缓冲）」方向。纯函数便于单测。
    /// </summary>
    public static bool IsPlaybackActive(VLCState state) =>
        state is VLCState.Playing or VLCState.Buffering or VLCState.Opening;

    /// <summary>
    /// 高速档是否需要下发「禁用音轨」：仅当倍速 &gt; 4×、尚未禁用、且处于 Playing
    /// （音轨此时才完成选择）时执行。纯函数便于单测。
    /// </summary>
    public static bool NeedsAudioDisable(double rate, bool alreadyDisabled, VLCState state) =>
        rate > HighRateAudioLimit && !alreadyDisabled && state == VLCState.Playing;

    public void Stop() => _mediaPlayer.Stop();
    public void NextFrame() => _mediaPlayer.NextFrame();

    // --- 后台控制线程（v1.7.3）：串行执行阻塞性原生调用 ---

    /// <summary>请求重新应用高速档音轨策略（Playing/Rate 变更触发）。</summary>
    private void RequestAudioPolicy()
    {
        lock (_controlSync) _policyApplyPending = true;
        _controlSignal.Set();
    }

    private void ControlLoop()
    {
        while (true)
        {
            _controlSignal.WaitOne();
            while (true)
            {
                double rate;
                bool applyRate, applyPolicy, resetPolicy, hasAudioTrack, hasPause, pause, hasSeek;
                int audioTrack;
                long seekMs;
                lock (_controlSync)
                {
                    if (_stopControl) return;
                    rate = _desiredRate;
                    applyRate = _rateApplyPending;
                    applyPolicy = _policyApplyPending;
                    resetPolicy = _resetPolicyPending;
                    hasAudioTrack = _hasPendingAudioTrack;
                    hasPause = _pauseApplyPending;
                    pause = _pendingPause;
                    audioTrack = _pendingAudioTrack;
                    hasSeek = _seekApplyPending;
                    seekMs = _seekTargetMs;
                    _rateApplyPending = false;
                    _policyApplyPending = false;
                    _resetPolicyPending = false;
                    _hasPendingAudioTrack = false;
                    _pauseApplyPending = false;
                    _seekApplyPending = false;
                    if (!applyRate && !applyPolicy && !resetPolicy && !hasAudioTrack && !hasPause && !hasSeek)
                        break;
                }

                try
                {
                    if (resetPolicy)
                    {
                        lock (_controlSync)
                        {
                            _savedAudioTrack = null;
                            _audioDisabledForHighRate = false;
                            _rateEstablished = false;
                        }
                    }
                    if (hasAudioTrack) _mediaPlayer.SetAudioTrack(audioTrack);
                    if (applyRate || applyPolicy)
                    {
                        var policyChanged = ApplyHighRateAudioPolicy(rate);
                        bool established;
                        lock (_controlSync) established = _rateEstablished;
                        if ((applyRate || !established || policyChanged) && HasInput)
                        {
                            _mediaPlayer.SetRate((float)rate);
                            lock (_controlSync) _rateEstablished = true;
                        }
                    }
                    if (hasSeek)
                    {
                        var state = _mediaPlayer.State;
                        if (state is VLCState.NothingSpecial or VLCState.Stopped or VLCState.Ended or VLCState.Error)
                        {
                            // 起播前 input 未创建，Time 赋值无效：延迟到 Playing 时补发
                            lock (_controlSync) _deferredSeekMs = seekMs;
                        }
                        else
                        {
                            _mediaPlayer.Time = seekMs;
                            if (!IsPlaybackActive(state)) _mediaPlayer.NextFrame();
                        }
                    }
                    if (hasPause)
                    {
                        if (pause)
                        {
                            _mediaPlayer.SetPause(true);
                        }
                        else
                        {
                            if (_mediaPlayer.State is not VLCState.Paused)
                            {
                                lock (_controlSync) _rateEstablished = false;
                            }
                            _mediaPlayer.Play();
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("[VideoPlayback] 倍速/音轨/暂停/seek 控制失败", ex);
                }
            }
        }
    }

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
    /// <para>v1.7.4：原生赋值下沉到后台控制线程；UI 线程只更新期望位置并唤醒，
    /// 连续快进/后退按「最后一次生效」合并，不会被 input 线程阻塞。</para>
    /// </summary>
    private void SeekToMs(long targetMs)
    {
        // 倍速重缓冲期间 is_playing 为 false，但实际仍在播放方向：
        // 用状态机判定，避免误走「暂停态」分支导致画面抖动
        var wasPlaying = IsPlaybackActive(_mediaPlayer.State);
        _pendingSeekTargetMs = targetMs;
        _pendingSeekAtUtc = DateTime.UtcNow;
        lock (_controlSync)
        {
            _seekTargetMs = targetMs;
            _seekApplyPending = true;
        }
        _controlSignal.Set();
        if (!wasPlaying)
        {
            // 暂停态：原生补帧由控制线程随 seek 一并执行；先手动通知 UI 刷新播放头
            PositionChanged?.Invoke(this, TimeSpan.FromMilliseconds(targetMs));
        }
    }

    /// <summary>
    /// 音频时钟会把有效倍速钳制在约 4×（LibVLC 3 aout 限制）。
    /// 高于该阈值时禁用音轨（同步静音、解除钳制），回落时恢复原音轨。
    /// </summary>
    private const double HighRateAudioLimit = 4.0;

    /// <summary>
    /// 设置倍速：UI 线程只写期望值并唤醒控制线程，阻塞性原生调用全部下沉（v1.7.3）。
    /// 连续切换（如按住 ↑/↓）按「最后一次生效」合并，不会排队堆积。
    /// </summary>
    public void SetRate(double rate)
    {
        // 服务层兜底 0.25~32（VM 层已按设置的最高倍率钳制，勿在此收紧）
        var clamped = Math.Clamp(rate, 0.25, 32.0);
        lock (_controlSync)
        {
            _desiredRate = clamped;
            _rateApplyPending = true;
        }
        _controlSignal.Set();
    }

    /// <summary>
    /// 按倍速应用音轨策略：&gt;4× 禁用音轨真实加速，≤4× 恢复音轨。
    /// 仅在后台控制线程调用（内部含阻塞性原生调用与 _savedAudioTrack 状态迁移）。
    /// </summary>
    /// <returns>是否实际改动了音轨（调用方需据此重新下发倍速）。</returns>
    private bool ApplyHighRateAudioPolicy(double rate)
    {
        if (rate > HighRateAudioLimit)
        {
            bool alreadyDisabled;
            lock (_controlSync) alreadyDisabled = _audioDisabledForHighRate;
            // 音轨要到 Playing 才完成选择：Opening/Buffering 阶段不尝试禁用，
            // 否则会误记「已禁用」而错过 Playing 时的重试（高倍速记忆还原场景）
            if (!NeedsAudioDisable(rate, alreadyDisabled, _mediaPlayer.State)) return false;
            var current = _mediaPlayer.AudioTrack;
            lock (_controlSync)
            {
                if (current >= 0) _savedAudioTrack = current;
                _audioDisabledForHighRate = true;
            }
            _mediaPlayer.SetAudioTrack(-1);
            return true;
        }

        int? track;
        lock (_controlSync)
        {
            if (!_audioDisabledForHighRate) return false;
            _audioDisabledForHighRate = false;
            track = _savedAudioTrack;
            _savedAudioTrack = null;
        }
        // 高倍速起播时可能没读到真实音轨 id：回落到第一音轨（VLC 音轨 id 从 1 开始）
        _mediaPlayer.SetAudioTrack(track ?? 1);
        return true;
    }

    /// <summary>当前是否已有可用 input（Opening 阶段尚未就绪，不下发倍速）。</summary>
    private bool HasInput =>
        _mediaPlayer.State is VLCState.Playing or VLCState.Paused or VLCState.Buffering;

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
        // AddSlave 后字幕轨立即进入 SpuDescription（同一事件循环），
        // 通知 VM 层刷新轨道菜单
        RaiseOnUi(() => TracksChanged?.Invoke(this, EventArgs.Empty));
    }

    // --- 轨道枚举与切换（v1.5.0）---

    public IReadOnlyList<TrackInfo> GetAudioTracks()
    {
        // 未播放/无媒体时 native 返回空数组，无需额外守卫
        return _mediaPlayer.AudioTrackDescription
            .Select(t => new TrackInfo(t.Id, t.Name))
            .ToList();
    }

    public IReadOnlyList<TrackInfo> GetSubtitleTracks()
    {
        // VLC 的 SpuDescription 自带 id=-1「Disable」项，VM 层统一跳过，
        // 由「关闭字幕」入口表达，避免重复
        return _mediaPlayer.SpuDescription
            .Where(t => t.Id >= 0)
            .Select(t => new TrackInfo(t.Id, t.Name))
            .ToList();
    }

    public int CurrentAudioTrack => _mediaPlayer.AudioTrack;
    public int CurrentSubtitleTrack => _mediaPlayer.Spu;

    public void SetAudioTrack(int id)
    {
        lock (_controlSync)
        {
            // 高倍速禁音轨策略期间（>4×）：真实音轨被禁用，仅记录用户选择，
            // 回落到 ≤4× 时由 ApplyHighRateAudioPolicy 恢复该选择
            if (_desiredRate > HighRateAudioLimit)
            {
                _savedAudioTrack = id;
                return;
            }
            _savedAudioTrack = null; // 用户显式选择后，策略记忆失效
            _pendingAudioTrack = id;
            _hasPendingAudioTrack = true;
        }
        // 原生切换下沉到控制线程，避免 UI 线程阻塞（v1.7.3）
        _controlSignal.Set();
    }

    public void SetSubtitleTrack(int id) => _mediaPlayer.SetSpu(id);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // 先停控制线程，再释放原生对象，避免释放期间仍有原生调用在途
        lock (_controlSync) _stopControl = true;
        _controlSignal.Set();
        var joined = _controlThread.Join(2000);
        if (!joined)
        {
            // 控制线程仍阻塞在原生调用内：放弃释放原生资源（交给进程退出回收），
            // 避免与在途调用竞态导致访问冲突
            return;
        }
        try { _mediaPlayer.Stop(); } catch { }
        _mediaPlayer.Dispose();
        _media?.Dispose();
        _libVlc.Dispose();
        _controlSignal.Dispose();
    }
}
