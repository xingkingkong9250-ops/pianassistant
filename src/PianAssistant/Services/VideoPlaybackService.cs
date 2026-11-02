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

    // --- 位置/音量缓存（v1.7.6）---
    // UI 线程禁止直读 libvlc：get_time / get_volume 都是经 input / aout 线程同步
    // 往返的阻塞调用。位置缓存由 TimeChanged 原生回调第一时间写入，音量由
    // 设置时写入期望值，UI 侧一律读缓存。
    private long _cachedTimeMs;
    // 暂停态位置权威值（v1.15.7 精准截图）：LibVLC 在暂停态 seek 后不刷新 get_time，
    // 仍返回暂停瞬间的旧时间（实测滑块在 7.663s，get_time 仍报 0.250s），而画面已渲染
    // 到 seek 目标。该字段记录「暂停时刻的原生时间」或「暂停态 seek 目标」，供截图 /
    // 单帧步进使用；恢复播放、停止、换片时置 -1 失效。
    private long _pausedSeekTargetMs = -1;
    // 总时长缓存（v1.7.7 根因修复）：
    // Media.DurationChanged 在 LibVLCSharp 3.x 里不可靠——即使订阅了也经常不触发。
    // 改为由后台控制线程读 _mediaPlayer.Length（来自 input 线程，demux 启动后即有值）
    // 并写入此缓存，UI 侧一律读缓存。
    private long _cachedLengthMs;
    private bool _lengthQueryPending;
    private int _cachedVolume = 100;
    private int _desiredVolume = -1;
    private bool _volumeApplyPending;
    // Playing 触发后 aout 重建需要时间，音量立即下发可能无效。用 DispatcherTimer
    // 延迟重发（v1.7.7 音频卡顿修复）
    private DispatcherTimer? _volumeReapplyTimer;
    private const int VolumeReapplyDelayMs = 300;
    // 连续 seek 合并窗口：长按方向键（50ms/次）时把 burst 内的 seek 收敛为
    // 每 ~120ms 一次原生赋值，减少对解码管线的打断（v1.7.6）
    private const int SeekCoalesceWindowMs = 120;
    private const int SeekBurstWindowMs = 400;
    private DateTime _lastSeekApplyUtc = DateTime.MinValue;

    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? EndReached;
    public event EventHandler? Playing;
    public event EventHandler? Paused;
    public event EventHandler? Stopped;
    public event EventHandler? TracksChanged;

    public bool IsPlaying => _mediaPlayer.IsPlaying;
    public TimeSpan Position
    {
        get
        {
            // v1.7.7 修正：
            // 原先直接读 _cachedTimeMs（TimeChanged 回调写入），撤掉 SeekToMs 的预写后，
            // 暂停态 seek → NextFrame 还没触发 TimeChanged 前，缓存仍是旧值。此时用户立刻
            // 截图 / 跳转，Position 返回旧时间码导致位置偏差。
            // 改用 ResolveSeekBaseMs：settle 窗口内存在 pending seek 目标时用它（此时
            // PositionChanged 手动通知也刚触发，UI 播放头已对齐），否则回落到缓存。
            // v1.15.7：暂停态位置优先——暂停态 seek 后原生 get_time 不刷新（仍返回暂停
            // 瞬间的旧值），画面却已渲染到 seek 目标；此时缓存/pending 都会给出旧位置
            var pausedMs = Volatile.Read(ref _pausedSeekTargetMs);
            if (pausedMs >= 0) return TimeSpan.FromMilliseconds(pausedMs);
            var cached = Volatile.Read(ref _cachedTimeMs);
            var sincePending = _pendingSeekTargetMs >= 0
                ? (DateTime.UtcNow - _pendingSeekAtUtc).TotalMilliseconds
                : double.MaxValue;
            var ms = ResolveSeekBaseMs(_pendingSeekTargetMs, sincePending, cached);
            return TimeSpan.FromMilliseconds(ms < 0 ? 0 : ms);
        }
    }
    public TimeSpan GetPrecisePosition()
    {
        // v1.15.7：暂停态 seek 后 get_time 仍返回暂停瞬间的旧时间（实测滑块 7.663s、
        // 截图却落在 0.250s），必须优先使用暂停态权威位置
        var pausedMs = Volatile.Read(ref _pausedSeekTargetMs);
        if (pausedMs >= 0) return TimeSpan.FromMilliseconds(pausedMs);

        // 待生效的 seek 目标优先：原生 Time 尚未更新时直读仍是旧值（如暂停态 seek）
        var pendingMs = _pendingSeekTargetMs;
        if (pendingMs >= 0 &&
            (DateTime.UtcNow - _pendingSeekAtUtc).TotalMilliseconds <= SeekSettleWindowMs)
            return TimeSpan.FromMilliseconds(pendingMs);

        try
        {
            var ms = _mediaPlayer.Time;
            if (ms >= 0)
            {
                Volatile.Write(ref _cachedTimeMs, ms);
                return TimeSpan.FromMilliseconds(ms);
            }
        }
        catch (Exception ex)
        {
            // 直读原生时钟失败不影响截图主流程：退回缓存值
            AppLog.Error("[VideoPlayback] 精确位置读取失败（退回缓存值）", ex);
        }
        var cached = Volatile.Read(ref _cachedTimeMs);
        return TimeSpan.FromMilliseconds(cached < 0 ? 0 : cached);
    }

    public TimeSpan Length
    {
        get
        {
            // v1.7.7：改读 DurationChanged 事件驱动的缓存。原先直读 _media.Duration，
            // 但 Media.Duration 只有在媒体解析完成后才被填充（不 Parse() 也不订阅
            // DurationChanged 时永远是 0）。现在缓存随事件实时刷新。
            var ms = Volatile.Read(ref _cachedLengthMs);
            return ms > 0 ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero;
        }
    }
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
        get => Volatile.Read(ref _cachedVolume);
        set
        {
            var v = Math.Clamp(value, 0, 100);
            Volatile.Write(ref _cachedVolume, v);
            // set_volume 同样经 aout 同步下发：拖动滑块时高频调用会拖住 UI 线程，
            // 与倍速/暂停一样下沉到后台控制线程执行（v1.7.6）
            lock (_controlSync)
            {
                _desiredVolume = v;
                _volumeApplyPending = true;
            }
            _controlSignal.Set();
        }
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
        _mediaPlayer.TimeChanged += (_, e) =>
        {
            // 原生回调线程第一时间写位置缓存（早于 marshal 到 UI），
            // 保证 UI 侧 Position 读到的值与事件同源且无阻塞（v1.7.6）
            if (e.Time >= 0) Volatile.Write(ref _cachedTimeMs, e.Time);
            RaiseOnUi(() =>
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
        };
        _mediaPlayer.EndReached += (_, _) => RaiseOnUi(() =>
        {
            _pendingSeekTargetMs = -1;
            Volatile.Write(ref _pausedSeekTargetMs, -1);
            EndReached?.Invoke(this, EventArgs.Empty);
        });
        // Parse track info (fps/dimensions) once playback starts — tracks
        // are demuxed by then. Replaces ESAdded (not available in 3.8.2).
        _mediaPlayer.Playing += (_, _) => RaiseOnUi(() =>
        {
            // 真正恢复播放：原生时钟恢复刷新，暂停态权威位置失效（v1.15.7）
            Volatile.Write(ref _pausedSeekTargetMs, -1);
            OnMediaParsed();
            // 音轨此刻才完成选择：若当前处于高速档（如打开时按倍速记忆恢复 8×），
            // 需再次应用音轨策略禁用音频，解除 4× 时钟钳制。
            // 原生调用下沉到后台控制线程，避免 UI 线程阻塞（v1.7.3）
            RequestAudioPolicy();
            // v1.7.7：Playing 触发时 input 线程 demux 已启动，此时 _mediaPlayer.Length 应该有值了——
            // 再设置一次 pending，即使 Open 那次 ControlLoop 先被唤醒时 len 还为 0，这次也能查到
            lock (_controlSync) { _lengthQueryPending = true; }
            _controlSignal.Set(); // 唤醒 control thread 去查长度
            AppLog.Info($"[Playing] 已 set _lengthQueryPending + Set signal (state={_mediaPlayer.State})");
            // 新媒体的 aout 会重建，音量需要重新下发（与倍速/音轨同理下沉，v1.7.6）。
            // v1.7.7：延后 300ms 等 aout 稳定再下发 —— Playing 触发瞬间 aout 可能还在重建，
            // 立即下发 Volume 会被丢弃/部分生效，导致暂停后重放音频前几百毫秒卡顿或失声
            ScheduleVolumeReapply();
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
            Volatile.Write(ref _pausedSeekTargetMs, -1);
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

    /// <summary>
    /// 计算相对 seek 的基准位置（纯函数，便于单测）：抑制窗内且存在未到位目标时
    /// 用目标值，否则用缓存位置——**绝不直读原生 Time**（v1.7.6）。
    /// </summary>
    public static long ResolveSeekBaseMs(
        long pendingTargetMs, double sinceSeekMs, long cachedMs,
        double windowMs = SeekSettleWindowMs)
        => pendingTargetMs >= 0 && sinceSeekMs <= windowMs ? pendingTargetMs : cachedMs;

    /// <summary>
    /// 位置基准（纯函数，便于单测）：暂停态权威位置优先（v1.15.7），其次按
    /// pending seek 抑制窗规则，最后用缓存。暂停态 seek 后原生 get_time 不刷新，
    /// 只有这个权威值能对应当前画面。
    /// </summary>
    public static long ResolvePausedBaseMs(
        long pausedTargetMs, long pendingTargetMs, double sinceSeekMs, long cachedMs)
        => pausedTargetMs >= 0
            ? pausedTargetMs
            : ResolveSeekBaseMs(pendingTargetMs, sinceSeekMs, cachedMs);

    /// <summary>
    /// 是否需要进入 seek 合并等待（纯函数，便于单测）：距上次原生下发过近即为
    /// burst（长按方向键），应合并后续请求（v1.7.6）。
    /// </summary>
    public static bool ShouldCoalesceSeek(
        double sinceLastApplyMs, double burstWindowMs = SeekBurstWindowMs)
        => sinceLastApplyMs < burstWindowMs;

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
        // v1.7.7：先取消旧 media 的 DurationChanged 订阅（避免内存泄漏 + 跨素材串事件）
        if (_media is not null) _media.DurationChanged -= OnMediaDurationChanged;
        _media = new Media(_libVlc, new Uri(path));
        // v1.7.7：订阅 DurationChanged —— Media.Duration 只有在媒体解析完成后才被填充，
        // 这是它唯一可靠的更新来源。没有这行时 Length 永远是 0
        _media.DurationChanged += OnMediaDurationChanged;
        _mediaPlayer.Media = _media;
        // 新素材音轨 id 无效化：高速档禁用的音轨记忆仅在同一素材内有效；
        // 策略状态由后台控制线程重置，避免与正在执行的倍速/音轨调用竞争
        lock (_controlSync)
        {
            _resetPolicyPending = true;
            _rateEstablished = false;
            _tracksRaisedForMedia = false;
            _deferredSeekMs = -1;
            _lengthQueryPending = true; // v1.7.7：Open 就请求一次 control thread 查长度
        }
        // 新素材从 0 开始：位置/时长缓存都清零，避免沿用上一素材的值（v1.7.6 + v1.7.7）
        Volatile.Write(ref _cachedTimeMs, 0);
        Volatile.Write(ref _cachedLengthMs, 0);
        Volatile.Write(ref _pausedSeekTargetMs, -1);
        _controlSignal.Set();
    }

    /// <summary>
    /// Media.Duration 事件回调（v1.7.7）——媒体解析完成后由 VLC 原生线程派发。
    /// Duration 是唯一可靠的总时长来源，之前既不 Parse() 也不订阅这个事件，
    /// 导致 _media.Duration 永远是 0，UI 上总时长显示为零。
    /// </summary>
    private void OnMediaDurationChanged(object? sender, MediaDurationChangedEventArgs e)
    {
        if (e.Duration <= 0) return;
        Volatile.Write(ref _cachedLengthMs, e.Duration);
        // 到 UI 线程通知 ViewModel 刷新 LengthDisplayString：
        // Length 本身是 getter 读缓存，PropertyChanged 要由 VM 自己发。
        // PlayerViewModel.OnPlaying 会每次调用 Length = _playback.Length（含 OnPropertyChanged），
        // 但 Playing 触发时机可能早于 DurationChanged，所以这里也主动 RaiseOnUi 让有订阅者能响应。
        RaiseOnUi(() => LengthChanged?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? LengthChanged;

    public void Play()
    {
        // 恢复播放：原生时钟恢复刷新，暂停态权威位置失效（v1.15.7）
        Volatile.Write(ref _pausedSeekTargetMs, -1);
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
        // 暂停生效后由控制线程写入暂停瞬间的原生时间（v1.15.7）；先失效旧值
        Volatile.Write(ref _pausedSeekTargetMs, -1);
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
        // 播放/暂停切换：暂停态权威位置先失效，暂停生效时由控制线程重建（v1.15.7）
        Volatile.Write(ref _pausedSeekTargetMs, -1);
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

    /// <summary>
    /// Playing 事件触发后延后下发音量（v1.7.7）——DispatcherTimer 单次触发，避免 aout
    /// 还在重建时下发 Volume 导致失声。高速档 Playing 可能反复触发，定时器已 pending 时
    /// 直接重置，保证以 Playing 回调的「最后一次」为准。
    /// </summary>
    private void ScheduleVolumeReapply()
    {
        RaiseOnUi(() =>
        {
            _volumeReapplyTimer ??= new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(VolumeReapplyDelayMs),
            };
            _volumeReapplyTimer.Tick -= OnVolumeReapplyTimerTick;
            _volumeReapplyTimer.Tick += OnVolumeReapplyTimerTick;
            _volumeReapplyTimer.Stop();
            _volumeReapplyTimer.Start();
        });
    }

    private void OnVolumeReapplyTimerTick(object? sender, EventArgs e)
    {
        _volumeReapplyTimer?.Stop();
        lock (_controlSync)
        {
            _desiredVolume = Volatile.Read(ref _cachedVolume);
            _volumeApplyPending = true;
        }
        _controlSignal.Set();
    }

    /// <summary>
    /// seek 合并（v1.7.6，v1.7.7 调整 bailout 条件）：仅在不处于 burst 时立即执行；
    /// 若距上次下发不足 <see cref="SeekBurstWindowMs"/>（长按方向键场景），最多再等
    /// <see cref="SeekCoalesceWindowMs"/> 收集后续请求，只下发最后一次目标。
    /// 单次点击 / 拖动落点不受影响，不额外增加延迟。
    /// <para>
    /// v1.7.7：合并循环新增「非 seek 请求到达即退出」的 bailout —— 避免控制线程被
    /// WaitOne 阻塞最多 120ms，期间 Play/Pause/Volume/Rate 等请求无法被及时处理，
    /// 叠加 VLC 解码压力容易引发崩溃。
    /// </para>
    /// </summary>
    private long CoalesceSeek(long seekMs)
    {
        if (!ShouldCoalesceSeek((DateTime.UtcNow - _lastSeekApplyUtc).TotalMilliseconds))
            return seekMs;

        var deadline = DateTime.UtcNow.AddMilliseconds(SeekCoalesceWindowMs);
        while (true)
        {
            var remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remaining <= 0) return seekMs;
            if (!_controlSignal.WaitOne(remaining)) return seekMs;
            lock (_controlSync)
            {
                if (_stopControl) return seekMs;
                // v1.7.7：非 seek 请求到达 → 立即退出合并循环，优先处理它们
                if (_rateApplyPending || _pauseApplyPending || _volumeApplyPending ||
                    _hasPendingAudioTrack || _resetPolicyPending)
                    return seekMs;
                if (!_seekApplyPending) return seekMs; // 无后续 seek 了：落本次
                seekMs = _seekTargetMs;
                _seekApplyPending = false;
            }
        }
    }

    private void ControlLoop()
    {
        while (true)
        {
            _controlSignal.WaitOne();
            while (true)
            {
                double rate;
                bool applyRate, applyPolicy, resetPolicy, hasAudioTrack, hasPause, pause, hasSeek, applyVolume, hasLengthQuery;
                int audioTrack, volume;
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
                    applyVolume = _volumeApplyPending;
                    volume = _desiredVolume;
                    hasLengthQuery = _lengthQueryPending;
                    _rateApplyPending = false;
                    _policyApplyPending = false;
                    _resetPolicyPending = false;
                    _hasPendingAudioTrack = false;
                    _pauseApplyPending = false;
                    _seekApplyPending = false;
                    _volumeApplyPending = false;
                    if (!applyRate && !applyPolicy && !resetPolicy && !hasAudioTrack && !hasPause && !hasSeek && !applyVolume && !hasLengthQuery)
                        break;
                }

                try
                {
                    // v1.7.7：从 input 线程读总时长（Media.DurationChanged 在 LibVLCSharp 3.x 里不可靠）
                    if (hasLengthQuery)
                    {
                        // 小 retry 循环：最多 3 次、每次等 100ms（后台线程安全 Sleep）
                        var len = 0L;
                        for (var i = 0; i < 3; i++)
                        {
                            len = _mediaPlayer.Length;
                            AppLog.Info($"[LengthQuery] attempt {i+1}: len={len}ms state={_mediaPlayer.State}");
                            if (len > 0) break;
                            Thread.Sleep(100);
                        }
                        lock (_controlSync) _lengthQueryPending = false; // 无论结果，pending 清掉
                        if (len > 0)
                        {
                            Volatile.Write(ref _cachedLengthMs, len);
                            AppLog.Info($"[LengthQuery] SUCCESS _cachedLengthMs={len}ms");
                            RaiseOnUi(() => LengthChanged?.Invoke(this, EventArgs.Empty));
                        }
                        else
                        {
                            AppLog.Info($"[LengthQuery] FAILED after 3 attempts");
                        }
                    }
                    if (resetPolicy)
                    {
                        lock (_controlSync)
                        {
                            _savedAudioTrack = null;
                            _audioDisabledForHighRate = false;
                            _rateEstablished = false;
                        }
                    }
                    if (applyVolume && volume >= 0)
                    {
                        _mediaPlayer.Volume = volume;
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
                        // 长按方向键时把 burst 内的连续 seek 合并为一次原生赋值（v1.7.6）
                        seekMs = CoalesceSeek(seekMs);
                        _lastSeekApplyUtc = DateTime.UtcNow;
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
                            // 暂停瞬间的原生时间即画面位置：暂停态 seek 后 get_time
                            // 不再刷新，这里记录权威值供截图使用（v1.15.7）
                            var pausedMs = _mediaPlayer.Time;
                            if (pausedMs >= 0) Volatile.Write(ref _pausedSeekTargetMs, pausedMs);
                        }
                        else
                        {
                            Volatile.Write(ref _pausedSeekTargetMs, -1);
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
        // v1.7.7：用缓存而不是 _media.Duration（后者 demux 前一直为 0）
        var lengthMs = Volatile.Read(ref _cachedLengthMs);
        if (lengthMs <= 0) return;
        SeekToMs(Math.Clamp((long)position.TotalMilliseconds, 0, lengthMs));
    }

    public void SeekRelative(TimeSpan delta)
    {
        if (Fps <= 0) return;
        // 连续触发（长按）时基准用上次 seek 目标叠加，避免偏差累积与来回跳动。
        // 基准位置取缓存值而非直读原生 Time：后者经 input 线程同步往返，
        // 大文件解码繁忙时会把 UI 线程卡住（v1.7.6）
        // v1.15.7：暂停态 seek 后原生时钟不刷新，基准改用暂停态权威位置
        var baseMs = ResolvePausedBaseMs(Volatile.Read(ref _pausedSeekTargetMs),
            _pendingSeekTargetMs,
            (DateTime.UtcNow - _pendingSeekAtUtc).TotalMilliseconds,
            Volatile.Read(ref _cachedTimeMs));
        var targetMs = baseMs + (long)delta.TotalMilliseconds;
        if (targetMs < 0) targetMs = 0;
        var lengthMs = Volatile.Read(ref _cachedLengthMs);
        if (lengthMs > 0 && targetMs > lengthMs) targetMs = lengthMs;
        SeekToMs(targetMs);
    }

    public void SeekPercent(double percent)
    {
        if (_media is null) return;
        // v1.7.7：用缓存
        var lengthMs = Volatile.Read(ref _cachedLengthMs);
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
        // v1.7.7：**撤掉预写缓存**——原先把 targetMs 直接写进 _cachedTimeMs 导致 UI 侧
        // Position 立刻跳到目标值，但画面还停在旧帧（VLC seek 异步生效），这就是用户反馈
        // 的「列表缩略图看着对，跳到播放窗口就不对」的根因。现在缓存保持上一个 TimeChanged
        // 回调的真实值，seek 到位后 TimeChanged 会回报真实位置，Position 自然对齐。
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
            // v1.15.7：暂停态 seek 的目标就是画面将呈现的位置。原生 get_time 在暂停态
            // 不随 seek 刷新（实测仍返回暂停瞬间的旧值），必须记录该目标供截图使用
            Volatile.Write(ref _pausedSeekTargetMs, targetMs);
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
