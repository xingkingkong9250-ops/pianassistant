using System.Collections.ObjectModel;
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
    private bool _disposed;

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
    // PR 风格裁切点
    private TimeSpan? _selectedSegmentStart;
    private TimeSpan? _selectedSegmentEnd;

    private static readonly double[] RateSteps =
        { 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 4.0 };

    public PlayerViewModel(
        IVideoPlayback playback,
        ScreenshotService screenshots,
        ClipExtractionService clips)
    {
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(screenshots);
        ArgumentNullException.ThrowIfNull(clips);
        _playback = playback;
        _screenshots = screenshots;
        _clips = clips;

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
        ClearCutPointsCommand = new RelayCommand(ClearCutPoints);
        ExportSelectedSegmentCommand = new AsyncRelayCommand(
            ExportSelectedSegmentAsync, () => !_isExtracting && HasSelectedSegment);
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
            var clamped = Math.Clamp(value, 0.25, 4.0);
            if (SetProperty(ref _rate, clamped))
                _playback.SetRate(clamped);
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

    // --- Events ---

    public event EventHandler<ScreenshotEntry>? ScreenshotCaptured;
    public event EventHandler<ClipExtractedEventArgs>? ClipExtracted;
    public event EventHandler<ClipExtractedEventArgs>? SegmentExported;
    public event EventHandler? FullscreenToggleRequested;
    public event EventHandler? FullscreenExitRequested;

    // --- Methods ---

    public void Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _playback.Open(path);
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

    /// <summary>Seek to a percentage of the total length (0..1).</summary>
    public void SeekPercent(double percent)
    {
        _playback.SeekPercent(percent);
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
            if (RateSteps[i] > _rate + 0.001)
            {
                Rate = RateSteps[i];
                return;
            }
        }
        Rate = RateSteps[^1];
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

    private void MarkIn() => InPoint = _position;

    private void MarkOut() => OutPoint = _position;

    private void ClearInOut()
    {
        InPoint = null;
        OutPoint = null;
    }

    private async Task CaptureScreenshotAsync()
    {
        if (_playback.CurrentPath is null) return;
        try
        {
            var entry = await Task.Run(() => _screenshots.Capture());
            ScreenshotCaptured?.Invoke(this, entry);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlayerVM] CaptureScreenshot failed: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"[PlayerVM] ExtractClip failed: {ex.Message}");
        }
        finally
        {
            IsExtracting = false;
            ExtractProgress = 0;
        }
    }

    // --- PR 风格裁切 ---

    private void AddCutPoint()
    {
        var pos = _position;
        // 避免重复添加极接近的点（<100ms）
        if (CutPoints.Any(c => Math.Abs((c - pos).TotalMilliseconds) < 100))
            return;
        // 保持有序
        var index = CutPoints.TakeWhile(c => c < pos).Count();
        CutPoints.Insert(index, pos);
    }

    private void RemoveLastCutPoint()
    {
        if (CutPoints.Count > 0)
            CutPoints.RemoveAt(CutPoints.Count - 1);
        SelectedSegmentStart = null;
        SelectedSegmentEnd = null;
        ExportSelectedSegmentCommand.RaiseCanExecuteChanged();
    }

    private void ClearCutPoints()
    {
        CutPoints.Clear();
        SelectedSegmentStart = null;
        SelectedSegmentEnd = null;
        ExportSelectedSegmentCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 根据点击位置确定所属段：找到前后两个相邻裁切点。
    /// 若只有一个裁切点，段为 [该点, 视频尾]。
    /// 若无裁切点，不选中。
    /// </summary>
    public void SelectSegmentAt(TimeSpan position)
    {
        if (CutPoints.Count == 0) return;
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
            System.Diagnostics.Debug.WriteLine($"[PlayerVM] ExportSegment failed: {ex.Message}");
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
            _playback.Seek(TimeSpan.Zero);
            _playback.Play();
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
