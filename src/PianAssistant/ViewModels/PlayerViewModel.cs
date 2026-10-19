using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using PianAssistant.Helpers;
using PianAssistant.Models;
using PianAssistant.Services;

namespace PianAssistant.ViewModels;

/// <summary>
/// Core playback ViewModel. Wraps <see cref="IVideoPlayback"/> with
/// two-way Position binding for the timeline, frame-precise timecode
/// display, in/out marking, and async screenshot/clip commands.
/// </summary>
public sealed class PlayerViewModel : ObservableObject, IDisposable
{
    private readonly IVideoPlayback _playback;
    private readonly ScreenshotService _screenshots;
    private readonly ClipExtractionService _clips;
    private readonly Func<int> _getSeekStepFrames;
    private readonly Func<double> _getRateStep;
    private readonly Func<double> _getMaxRate;
    private bool _disposed;
    // 每个文件路径独立保存的播放倍速
    private readonly Dictionary<string, double> _rateMemory = new();

    // Position binding state:
    // _isUpdatingFromPlayback guards against Seek→PositionChanged→Seek loops.
    private bool _isUpdatingFromPlayback;
    private TimeSpan _position = TimeSpan.Zero;
    private TimeSpan _length = TimeSpan.Zero;
    private bool _isPlaying;
    private double _rate = 1.0;
    private TimeSpan? _inPoint;
    private TimeSpan? _outPoint;
    private bool _loopMode;
    private bool _isExtracting;
    private double _extractProgress;
    private int _volume = 100;
    private bool _isMuted;
    // 音量持久化回调（Shell 侧防抖写 config.json；null 表示不持久化，测试用）
    private readonly Action<int>? _onVolumePersisted;
    // PR 风格裁切点
    private TimeSpan? _selectedSegmentStart;
    private TimeSpan? _selectedSegmentEnd;

    private static readonly double[] RateSteps =
        { 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0, 6.0, 8.0, 10.0, 12.0, 16.0, 20.0, 32.0 };

    public PlayerViewModel(
        IVideoPlayback playback,
        ScreenshotService screenshots,
        ClipExtractionService clips,
        Func<int> getSeekStepFrames,
        Func<double> getRateStep,
        Func<double> getMaxRate,
        int initialVolume = 100,
        Action<int>? volumePersisted = null)
    {
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(screenshots);
        ArgumentNullException.ThrowIfNull(clips);
        _playback = playback;
        _screenshots = screenshots;
        _clips = clips;
        _getSeekStepFrames = getSeekStepFrames ?? (() => 5);
        _getRateStep = getRateStep ?? (() => 0.25);
        _getMaxRate = getMaxRate ?? (() => 8.0);
        _onVolumePersisted = volumePersisted;
        // 软件级音量记忆：启动时恢复上次音量（config.json，跨视频保持）
        _volume = Math.Clamp(initialVolume, 0, 100);
        _playback.Volume = _volume;

        // Wire playback events.
        _playback.PositionChanged += OnPositionChanged;
        _playback.Playing += OnPlaying;
        _playback.Paused += OnPaused;
        _playback.Stopped += OnStopped;
        _playback.EndReached += OnEndReached;

        // Commands.
        PlayPauseCommand = new RelayCommand(() => _playback.TogglePlayPause());
        NextFrameCommand = new RelayCommand(() => _playback.NextFrame());
        PreviousFrameCommand = new RelayCommand(PreviousFrame);
        SeekBackward1SecondCommand = new RelayCommand(
            () => _playback.SeekRelative(TimeSpan.FromSeconds(-1)));
        SeekForward1SecondCommand = new RelayCommand(
            () => _playback.SeekRelative(TimeSpan.FromSeconds(1)));
        SeekBackward10SecondsCommand = new RelayCommand(
            () => _playback.SeekRelative(TimeSpan.FromSeconds(-10)));
        SeekForward10SecondsCommand = new RelayCommand(
            () => _playback.SeekRelative(TimeSpan.FromSeconds(10)));
        SeekToStartCommand = new RelayCommand(
            () => _playback.Seek(TimeSpan.Zero));
        SeekToEndCommand = new RelayCommand(
            () => _playback.Seek(_length));
        IncreaseRateCommand = new RelayCommand(IncreaseRate);
        DecreaseRateCommand = new RelayCommand(DecreaseRate);
        ResetRateCommand = new RelayCommand(() => Rate = 1.0);
        MarkInCommand = new RelayCommand(MarkIn);
        MarkOutCommand = new RelayCommand(MarkOut);
        ClearInOutCommand = new RelayCommand(ClearInOut);
        CaptureScreenshotCommand = new AsyncRelayCommand(CaptureScreenshotAsync);
        ExtractClipCommand = new AsyncRelayCommand(
            ExtractClipAsync, () => !_isExtracting && HasCompleteClip);
        ToggleFullscreenCommand = new RelayCommand(
            () => FullscreenToggleRequested?.Invoke(this, EventArgs.Empty));
        ExitFullscreenCommand = new RelayCommand(
            () => FullscreenExitRequested?.Invoke(this, EventArgs.Empty));
        ToggleLoopCommand = new RelayCommand(
            () => LoopMode = !LoopMode);
        ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
        VolumeUpCommand = new RelayCommand(() => Volume = Math.Min(100, Volume + 10));
        VolumeDownCommand = new RelayCommand(() => Volume = Math.Max(0, Volume - 10));

        // PR 风格裁切命令
        AddCutPointCommand = new RelayCommand(AddCutPoint, () => HasMedia);
        RemoveLastCutPointCommand = new RelayCommand(RemoveLastCutPoint);
        ClearCutPointsCommand = new RelayCommand(
            // 清空全部标记（裁切点 + 截图标记）：经 Shell 确认后执行，不在 Player 层直接清
            () => ClearAllMarksRequested?.Invoke(this, EventArgs.Empty));
        ExportSelectedSegmentCommand = new AsyncRelayCommand(
            ExportSelectedSegmentAsync, () => !_isExtracting && HasSelectedSegment);
        // X 键：标记/取消标记当前选中片段（经 Shell 桥接到笔记 ☑ 勾选）
        ToggleMarkSelectedSegmentCommand = new RelayCommand(
            ExecuteToggleSegmentMark, () => HasSelectedSegment);

        // 方向键自定义步长命令（支持长按连续触发）
        SeekBackwardCustomCommand = new RelayCommand(SeekBackwardCustom, () => HasMedia);
        SeekForwardCustomCommand = new RelayCommand(SeekForwardCustom, () => HasMedia);
        IncreaseRateCustomCommand = new RelayCommand(IncreaseRateCustom);
        DecreaseRateCustomCommand = new RelayCommand(DecreaseRateCustom);
        // 字幕加载命令（接收文件路径参数）
        LoadSubtitleCommand = new AsyncRelayCommand<string>(LoadSubtitleAsync);
        // 百分比跳转命令（D1~D9）：参数为 0..1 的比例
        SeekPercentCommand = new RelayCommand(ExecuteSeekPercent);
    }

    // --- Properties ---

    /// <summary>
    /// Two-way bound position for the timeline. When the user drags the
    /// timeline, this setter calls <see cref="IVideoPlayback.Seek"/>.
    /// When playback advances, <see cref="OnPositionChanged"/> updates
    /// this with <see cref="_isUpdatingFromPlayback"/> to prevent loops.
    /// </summary>
    public TimeSpan Position
    {
        get => _position;
        set
        {
            if (SetProperty(ref _position, value))
            {
                OnPropertyChanged(nameof(PositionRatio));
                OnPropertyChanged(nameof(TimeCodeDisplayString));
                if (!_isUpdatingFromPlayback)
                    _playback.Seek(value);
            }
        }
    }

    /// <summary>0..1 ratio for slider/timeline binding.</summary>
    public double PositionRatio =>
        _length > TimeSpan.Zero
            ? Math.Clamp(_position.TotalMilliseconds / _length.TotalMilliseconds, 0, 1)
            : 0;

    public TimeSpan Length
    {
        get => _length;
        private set
        {
            if (SetProperty(ref _length, value))
            {
                OnPropertyChanged(nameof(PositionRatio));
                OnPropertyChanged(nameof(TimeCodeDisplayString));
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetProperty(ref _isPlaying, value);
    }

    public double Rate
    {
        get => _rate;
        set
        {
            var clamped = Math.Clamp(value, 0.25, CurrentMaxRate);
            if (SetProperty(ref _rate, clamped))
            {
                _playback.SetRate(clamped);
                OnPropertyChanged(nameof(RateText));
                OnPropertyChanged(nameof(IsRateNormal));
                OnPropertyChanged(nameof(IsRateAtMax));
            }
        }
    }

    /// <summary>倍速显示文本（如 "2.00×"）。</summary>
    public string RateText => $"{_rate:F2}×";

    /// <summary>是否为正常倍速（1.0×）。</summary>
    public bool IsRateNormal => Math.Abs(_rate - 1.0) < 0.001;

    /// <summary>当前配置的最高倍率上限（来自设置，2~32）。</summary>
    public double CurrentMaxRate => Math.Max(2.0, _getMaxRate());

    /// <summary>当前倍率是否已达到上限。</summary>
    public bool IsRateAtMax => _rate >= CurrentMaxRate - 0.001;

    /// <summary>更新最高倍率上限（设置窗调用）：刷新可达倍速列表并夹紧当前值。</summary>
    public void SetMaxRate(double max)
    {
        OnPropertyChanged(nameof(CurrentMaxRate));
        OnPropertyChanged(nameof(IsRateAtMax));
        OnPropertyChanged(nameof(AvailableRates));
        if (_rate > CurrentMaxRate)
        {
            Rate = CurrentMaxRate;
        }
        else
        {
            OnPropertyChanged(nameof(Rate));
            OnPropertyChanged(nameof(RateText));
        }
    }

    /// <summary>倍速下拉框可选项：全部预设中不超过上限的部分。</summary>
    public IReadOnlyList<RateOption> AvailableRates
    {
        get
        {
            var list = new List<RateOption>();
            foreach (var step in RateSteps)
            {
                if (step <= CurrentMaxRate + 0.001)
                {
                    list.Add(new RateOption(step));
                }
            }

            // 上限不在预设档位中时，追加自定义上限项
            if (list.Count == 0 || list[^1].Value < CurrentMaxRate - 0.001)
            {
                list.Add(new RateOption(CurrentMaxRate));
            }

            return list;
        }
    }

    public TimeSpan? InPoint
    {
        get => _inPoint;
        private set
        {
            if (SetProperty(ref _inPoint, value))
            {
                OnPropertyChanged(nameof(HasMarkedIn));
                OnPropertyChanged(nameof(HasCompleteClip));
                ExtractClipCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public TimeSpan? OutPoint
    {
        get => _outPoint;
        private set
        {
            if (SetProperty(ref _outPoint, value))
            {
                OnPropertyChanged(nameof(HasMarkedOut));
                OnPropertyChanged(nameof(HasCompleteClip));
                ExtractClipCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasMarkedIn => _inPoint is not null;
    public bool HasMarkedOut => _outPoint is not null;

    /// <summary>True when both in and out are set and out > in.</summary>
    public bool HasCompleteClip =>
        _inPoint is { } inP && _outPoint is { } outP && outP > inP;

    public bool LoopMode
    {
        get => _loopMode;
        set => SetProperty(ref _loopMode, value);
    }

    public int Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (SetProperty(ref _volume, clamped))
            {
                _playback.Volume = clamped;
                if (clamped > 0 && _isMuted)
                    IsMuted = false;
                // 软件级音量记忆：变化即通知持久化（Shell 侧防抖落盘）
                _onVolumePersisted?.Invoke(clamped);
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
                _playback.IsMuted = value;
        }
    }

    // --- PR 风格裁切点 ---

    /// <summary>清空全部标记请求（裁切点 + 截图标记）：由 Shell 层弹确认后执行。</summary>
    public event EventHandler? ClearAllMarksRequested;

    /// <summary>标记/取消标记当前选中片段请求（X 键），携带段起止时间。</summary>
    public event EventHandler<(TimeSpan Start, TimeSpan End)>? ToggleSegmentMarkRequested;

    /// <summary>所有裁切点（按时间排序）。</summary>
    public ObservableCollection<TimeSpan> CutPoints { get; } = new();

    public TimeSpan? SelectedSegmentStart
    {
        get => _selectedSegmentStart;
        private set => SetProperty(ref _selectedSegmentStart, value);
    }

    public TimeSpan? SelectedSegmentEnd
    {
        get => _selectedSegmentEnd;
        private set => SetProperty(ref _selectedSegmentEnd, value);
    }

    /// <summary>是否已选中一段（start 和 end 都有值且 end > start）。</summary>
    public bool HasSelectedSegment =>
        _selectedSegmentStart is { } s && _selectedSegmentEnd is { } e && e > s;

    public bool IsExtracting
    {
        get => _isExtracting;
        private set
        {
            SetProperty(ref _isExtracting, value);
            ExtractClipCommand.RaiseCanExecuteChanged();
        }
    }

    public double ExtractProgress
    {
        get => _extractProgress;
        private set => SetProperty(ref _extractProgress, value);
    }

    /// <summary>Frame-precise timecode: HH:MM:SS:FF.</summary>
    public string TimeCodeDisplayString =>
        TimeCodeFormatter.FormatFrames(_position, _playback.Fps);

    public string LengthDisplayString =>
        TimeCodeFormatter.FormatFrames(_length, _playback.Fps);

    public double Fps => _playback.Fps;
    /// <summary>当前帧步长（从设置读取）。</summary>
    public int SeekStepFrames => _getSeekStepFrames();
    /// <summary>当前倍速步长（从设置读取）。</summary>
    public double RateStep => _getRateStep();
    public int VideoWidth => _playback.Width;
    public int VideoHeight => _playback.Height;
    public string? CurrentPath => _playback.CurrentPath;
    public bool HasMedia => _playback.CurrentPath is not null;

    // --- Commands ---

    public ICommand PlayPauseCommand { get; }
    public ICommand NextFrameCommand { get; }
    public ICommand PreviousFrameCommand { get; }
    public ICommand SeekBackward1SecondCommand { get; }
    public ICommand SeekForward1SecondCommand { get; }
    public ICommand SeekBackward10SecondsCommand { get; }
    public ICommand SeekForward10SecondsCommand { get; }
    public ICommand SeekToStartCommand { get; }
    public ICommand SeekToEndCommand { get; }
    public ICommand IncreaseRateCommand { get; }
    public ICommand DecreaseRateCommand { get; }
    public ICommand ResetRateCommand { get; }
    public ICommand MarkInCommand { get; }
    public ICommand MarkOutCommand { get; }
    public ICommand ClearInOutCommand { get; }
    public AsyncRelayCommand CaptureScreenshotCommand { get; }
    public AsyncRelayCommand ExtractClipCommand { get; }
    public ICommand ToggleFullscreenCommand { get; }
    public ICommand ExitFullscreenCommand { get; }
    public ICommand ToggleLoopCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand VolumeUpCommand { get; }
    public ICommand VolumeDownCommand { get; }
    public ICommand AddCutPointCommand { get; }
    public ICommand RemoveLastCutPointCommand { get; }
    public ICommand ClearCutPointsCommand { get; }
    public AsyncRelayCommand ExportSelectedSegmentCommand { get; }
    /// <summary>X 键：切换当前选中片段的标记状态（事件上行至 Shell → Notes）。</summary>
    public ICommand ToggleMarkSelectedSegmentCommand { get; }
    // 方向键自定义步长命令
    public ICommand SeekBackwardCustomCommand { get; }
    public ICommand SeekForwardCustomCommand { get; }
    public ICommand IncreaseRateCustomCommand { get; }
    public ICommand DecreaseRateCustomCommand { get; }
    // 字幕加载命令（参数为字幕文件路径）
    public AsyncRelayCommand<string> LoadSubtitleCommand { get; }
    /// <summary>百分比跳转命令（D1~D9，参数为 0..1 的比例）。</summary>
    public ICommand SeekPercentCommand { get; }

    // --- Events ---

    public event EventHandler<ScreenshotEntry>? ScreenshotCaptured;
    public event EventHandler<ClipExtractedEventArgs>? ClipExtracted;
    public event EventHandler<ClipExtractedEventArgs>? SegmentExported;
    /// <summary>操作失败时的友好中文提示（由 View/App 层订阅弹窗），避免静默失败。</summary>
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler? FullscreenToggleRequested;
    public event EventHandler? FullscreenExitRequested;
    /// <summary>
    /// 非循环模式下播放结束时触发，由 Shell 接管以推进播放列表。
    /// </summary>
    public event EventHandler? EndReached;

    // --- Methods ---

    public void Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // 先保存当前视频的倍速到记忆表
        SaveCurrentRate();
        _playback.Open(path);
        // 再从记忆表加载新视频的倍速（无记录则重置为 1.0）
        LoadRate(path);
        Length = _playback.Length;
        Position = TimeSpan.Zero;
        InPoint = null;
        OutPoint = null;
        CutPoints.Clear();
        SelectedSegmentStart = null;
        SelectedSegmentEnd = null;
        OnPropertyChanged(nameof(HasMedia));
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(Fps));
        OnPropertyChanged(nameof(VideoWidth));
        OnPropertyChanged(nameof(VideoHeight));
        OnPropertyChanged(nameof(LengthDisplayString));
    }

    /// <summary>将当前视频的倍速保存到记忆表（按路径索引）。</summary>
    public void SaveCurrentRate()
    {
        var path = _playback.CurrentPath;
        if (string.IsNullOrEmpty(path)) return;
        _rateMemory[path] = _rate;
    }

    /// <summary>从记忆表加载指定路径的倍速；无记录时重置为 1.0。</summary>
    public void LoadRate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Rate = _rateMemory.TryGetValue(path, out var rate) ? rate : 1.0;
    }

    /// <summary>加载外挂字幕文件到当前媒体。</summary>
    public void LoadSubtitle(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _playback.LoadSubtitle(path);
    }

    private async Task LoadSubtitleAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        await Task.Run(() => _playback.LoadSubtitle(path));
    }

    /// <summary>Seek to a percentage of the total length (0..1).</summary>
    public void SeekPercent(double percent)
    {
        _playback.SeekPercent(percent);
    }

    /// <summary>百分比跳转命令执行体：解析 CommandParameter 为 0..1 比例。</summary>
    private void ExecuteSeekPercent(object? parameter)
    {
        switch (parameter)
        {
            case double d:
                SeekPercent(d);
                break;
            case string s when double.TryParse(
                s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v):
                SeekPercent(v);
                break;
        }
    }

    private void PreviousFrame()
    {
        var fps = Math.Max(_playback.Fps, 1.0);
        _playback.SeekRelative(TimeSpan.FromSeconds(-1.0 / fps));
    }

    private void IncreaseRate()
    {
        for (int i = 0; i < RateSteps.Length; i++)
        {
            if (RateSteps[i] > _rate + 0.001 && RateSteps[i] <= CurrentMaxRate + 0.001)
            {
                Rate = RateSteps[i];
                return;
            }
        }

        Rate = CurrentMaxRate;
    }

    private void DecreaseRate()
    {
        for (int i = RateSteps.Length - 1; i >= 0; i--)
        {
            if (RateSteps[i] < _rate - 0.001)
            {
                Rate = RateSteps[i];
                return;
            }
        }

        Rate = RateSteps[0];
    }

    /// <summary>方向键左：按自定义帧数×倍率后退（长按连续触发）。</summary>
    private void SeekBackwardCustom()
    {
        var fps = Math.Max(_playback.Fps, 1.0);
        var frames = _getSeekStepFrames() * Math.Max(_rate, 1.0);
        var delta = TimeSpan.FromSeconds(-frames / fps);
        _playback.SeekRelative(delta);
    }

    /// <summary>方向键右：按自定义帧数×倍率快进（长按连续触发）。</summary>
    private void SeekForwardCustom()
    {
        var fps = Math.Max(_playback.Fps, 1.0);
        var frames = _getSeekStepFrames() * Math.Max(_rate, 1.0);
        var delta = TimeSpan.FromSeconds(frames / fps);
        _playback.SeekRelative(delta);
    }

    /// <summary>方向键上：按自定义步长增加倍速（长按连续触发）。</summary>
    private void IncreaseRateCustom()
    {
        var step = _getRateStep();
        Rate = Math.Clamp(_rate + step, 0.25, CurrentMaxRate);
    }

    /// <summary>方向键下：按自定义步长降低倍速（长按连续触发）。</summary>
    private void DecreaseRateCustom()
    {
        var step = _getRateStep();
        Rate = Math.Clamp(_rate - step, 0.25, CurrentMaxRate);
    }

    private void MarkIn() => InPoint = _position;

    private void MarkOut() => OutPoint = _position;

    /// <summary>直接设置入/出点（会话还原用；不依赖当前播放位置）。</summary>
    public void SetInOut(TimeSpan? inPoint, TimeSpan? outPoint)
    {
        InPoint = inPoint;
        OutPoint = outPoint;
    }

    private void ClearInOut()
    {
        InPoint = null;
        OutPoint = null;
    }

    private async Task CaptureScreenshotAsync()
    {
        if (_playback.CurrentPath is null) return;
        // 第一时间在 UI 线程锁定截图时间点：Task.Run 排队 + 线程池启动期间
        // 视频仍在播放，倍速下晚读会漂移数帧（8× 时窗口内可漂移 0.3s+），
        // 导致截取的帧晚于用户按下按键时看到的画面（v1.2.3）
        var position = _playback.Position;
        try
        {
            var entry = await Task.Run(() => _screenshots.CaptureAt(position));
            ScreenshotCaptured?.Invoke(this, entry);
        }
        catch (Exception ex)
        {
            AppLog.Error("[PlayerVM] 截图失败", ex);
            ErrorOccurred?.Invoke(this, $"截图失败：{ex.Message}");
        }
    }

    private async Task ExtractClipAsync()
    {
        if (!HasCompleteClip) return;
        var inP = _inPoint!.Value;
        var outP = _outPoint!.Value;
        var source = _playback.CurrentPath;
        if (source is null) return;

        IsExtracting = true;
        ExtractProgress = 0;
        try
        {
            var range = new ClipRange(source, inP, outP);
            var outputPath = await Task.Run(() => _clips.Extract(
                range, false, CancellationToken.None, p => ExtractProgress = p));
            ExtractProgress = 1;
            ClipExtracted?.Invoke(this, new ClipExtractedEventArgs(range, outputPath));
        }
        catch (Exception ex)
        {
            AppLog.Error("[PlayerVM] 截取片段失败", ex);
            ErrorOccurred?.Invoke(this, $"截取片段失败：{ex.Message}");
        }
        finally
        {
            IsExtracting = false;
            ExtractProgress = 0;
        }
    }

    // --- PR 风格裁切 ---

    /// <summary>在当前播放位置添加裁切点。</summary>
    private void AddCutPoint() => AddCutPointAt(_position);

    /// <summary>
    /// 在指定位置添加裁切点（批量指令/导入共用）：
    /// 距现有点 &lt;100ms 时忽略重复，并保持按时间有序插入。
    /// </summary>
    public void AddCutPointAt(TimeSpan pos)
    {
        if (CutPoints.Any(c => Math.Abs((c - pos).TotalMilliseconds) < 100))
            return;
        var index = CutPoints.TakeWhile(c => c < pos).Count();
        CutPoints.Insert(index, pos);
        // 播放头恰在新裁切点上：所在片段自动切换为裁切点之后的段
        UpdateSelectedSegmentFromPlayhead();
    }

    /// <summary>
    /// 重设全部裁切点：清空后按时间升序逐个加入。
    /// 集合变化经 CollectionChanged 自动同步时间轴与笔记面板分段列表。
    /// </summary>
    public void SetCutPoints(IEnumerable<TimeSpan> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        ClearCutPoints();
        foreach (var p in points.OrderBy(p => p))
            AddCutPointAt(p);
    }

    private void RemoveLastCutPoint()
    {
        if (CutPoints.Count > 0)
            CutPoints.RemoveAt(CutPoints.Count - 1);
        UpdateSelectedSegmentFromPlayhead();
    }

    /// <summary>
    /// 按值删除裁切点（笔记面板裁切点行删除用，W1-C2-D-06）：
    /// 容差 100ms 内取最近点；删除后播放头所在段自动重选（上下片段随之合并）。
    /// </summary>
    public bool RemoveCutPoint(TimeSpan pos)
    {
        var best = -1;
        var bestMs = double.MaxValue;
        for (var i = 0; i < CutPoints.Count; i++)
        {
            var ms = Math.Abs((CutPoints[i] - pos).TotalMilliseconds);
            if (ms < bestMs) { bestMs = ms; best = i; }
        }
        if (best < 0 || bestMs >= 100) return false;
        CutPoints.RemoveAt(best);
        UpdateSelectedSegmentFromPlayhead();
        return true;
    }

    /// <summary>清空裁切点与选中段（不含截图标记；Shell 确认后调用）。</summary>
    public void ClearCutPoints()
    {
        CutPoints.Clear();
        UpdateSelectedSegmentFromPlayhead();
    }

    /// <summary>
    /// 根据点击位置确定所属段：找到前后两个相邻裁切点。
    /// 若只有一个裁切点，段为 [该点, 视频尾]。
    /// 无裁切点时整片为一个片段（与自动选中一致）。
    /// </summary>
    public void SelectSegmentAt(TimeSpan position)
    {
        if (_length <= TimeSpan.Zero) return;
        // 找到 position 落在哪两个裁切点之间
        TimeSpan start = TimeSpan.Zero;
        TimeSpan end = _length;
        foreach (var cp in CutPoints)
        {
            if (cp <= position)
                start = cp;
            else
            {
                end = cp;
                break;
            }
        }
        // 如果 position 比所有裁切点都大，段为 [最后一个裁切点, 尾]
        // 如果 position 比所有裁切点都小，段为 [0, 第一个裁切点]
        // 上面循环已处理这两种情况
        SelectedSegmentStart = start;
        SelectedSegmentEnd = end;
        ExportSelectedSegmentCommand.RaiseCanExecuteChanged();
        ((RelayCommand)ToggleMarkSelectedSegmentCommand).RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 播放头所在片段自动选中：仅当所在段边界与当前选中不同才更新，
    /// 避免逐帧 SetProperty 触发时间轴重绘。
    /// 无裁切点时整片视为片段 1（与笔记面板 RecomputeSegments 分段一致）；
    /// 时长未知（VLC 尚未解析）时不选中。
    /// </summary>
    private void UpdateSelectedSegmentFromPlayhead()
    {
        if (_length <= TimeSpan.Zero) return;
        TimeSpan start = TimeSpan.Zero, end = _length;
        foreach (var cp in CutPoints)
        {
            if (cp <= _position) start = cp;
            else { end = cp; break; }
        }
        if (SelectedSegmentStart != start || SelectedSegmentEnd != end)
        {
            SelectedSegmentStart = start;
            SelectedSegmentEnd = end;
            ExportSelectedSegmentCommand.RaiseCanExecuteChanged();
            ((RelayCommand)ToggleMarkSelectedSegmentCommand).RaiseCanExecuteChanged();
        }
    }

    /// <summary>X 键执行：上报当前选中段起止，由 Shell 桥接到笔记勾选标记。</summary>
    private void ExecuteToggleSegmentMark()
    {
        if (!HasSelectedSegment) return;
        ToggleSegmentMarkRequested?.Invoke(this, (_selectedSegmentStart!.Value, _selectedSegmentEnd!.Value));
    }

    private async Task ExportSelectedSegmentAsync()
    {
        if (!HasSelectedSegment) return;
        var inP = _selectedSegmentStart!.Value;
        var outP = _selectedSegmentEnd!.Value;
        var source = _playback.CurrentPath;
        if (source is null) return;

        IsExtracting = true;
        ExtractProgress = 0;
        try
        {
            var range = new ClipRange(source, inP, outP);
            var outputPath = await Task.Run(() => _clips.Extract(
                range, false, CancellationToken.None, p => ExtractProgress = p));
            ExtractProgress = 1;
            SegmentExported?.Invoke(this, new ClipExtractedEventArgs(range, outputPath));
        }
        catch (Exception ex)
        {
            AppLog.Error("[PlayerVM] 导出选中片段失败", ex);
            ErrorOccurred?.Invoke(this, $"导出选中片段失败：{ex.Message}");
        }
        finally
        {
            IsExtracting = false;
            ExtractProgress = 0;
        }
    }

    // --- Playback event handlers ---

    private void OnPositionChanged(object? sender, TimeSpan e)
    {
        _isUpdatingFromPlayback = true;
        try
        {
            Position = e;
            // v0.21.0：播放头所在片段自动选中（滑块停在片段中即选中该片段）
            UpdateSelectedSegmentFromPlayhead();
        }
        finally
        {
            _isUpdatingFromPlayback = false;
        }
    }

    private void OnPlaying(object? sender, EventArgs e)
    {
        IsPlaying = true;
        if (_length == TimeSpan.Zero)
            Length = _playback.Length;
    }

    private void OnPaused(object? sender, EventArgs e) => IsPlaying = false;
    private void OnStopped(object? sender, EventArgs e) => IsPlaying = false;

    private void OnEndReached(object? sender, EventArgs e)
    {
        IsPlaying = false;
        if (_loopMode)
        {
            // 循环模式：回到开头继续播放
            _playback.Seek(TimeSpan.Zero);
            _playback.Play();
        }
        else
        {
            // 非循环模式：通知 Shell 推进播放列表
            EndReached?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _playback.PositionChanged -= OnPositionChanged;
        _playback.Playing -= OnPlaying;
        _playback.Paused -= OnPaused;
        _playback.Stopped -= OnStopped;
        _playback.EndReached -= OnEndReached;
    }
}

/// <summary>Event args carrying the clip range and resulting output path.</summary>
public sealed class ClipExtractedEventArgs : EventArgs
{
    public ClipRange Range { get; }
    public string OutputPath { get; }

    public ClipExtractedEventArgs(ClipRange range, string outputPath)
    {
        Range = range;
        OutputPath = outputPath;
    }
}

/// <summary>倍速下拉框选项：值 + 显示文本。</summary>
public sealed record RateOption(double Value)
{
    public string Label => $"{Value:0.0#}×";
}
