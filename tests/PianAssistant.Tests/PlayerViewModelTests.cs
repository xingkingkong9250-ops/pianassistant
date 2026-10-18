using System.IO;
using PianAssistant.Models;
using PianAssistant.Services;
using PianAssistant.Tests.Stubs;
using PianAssistant.ViewModels;
using Xunit;

namespace PianAssistant.Tests;

public class PlayerViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StubVideoPlayback _playback;
    private readonly FfmpegRunner _ffmpeg;
    private readonly ScreenshotService _screenshots;
    private readonly ClipExtractionService _clips;
    private readonly PlayerViewModel _vm;

    public PlayerViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-player-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var ffmpegPath = Path.Combine(_tempDir, "ffmpeg.exe");
        var ffprobePath = Path.Combine(_tempDir, "ffprobe.exe");
        File.WriteAllText(ffmpegPath, "stub");
        File.WriteAllText(ffprobePath, "stub");
        _ffmpeg = new FfmpegRunner(ffmpegPath, ffprobePath);
        _playback = new StubVideoPlayback { Length = TimeSpan.FromMinutes(3) };
        _screenshots = new ScreenshotService(_playback, _ffmpeg, _tempDir, "png");
        _clips = new ClipExtractionService(_ffmpeg, _tempDir);
        _vm = new PlayerViewModel(_playback, _screenshots, _clips, () => 5, () => 0.25, () => 8.0);
    }

    public void Dispose()
    {
        _vm.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void Open_SetsCurrentPathAndResetsPosition()
    {
        var path = Path.Combine(_tempDir, "video.mp4");
        File.WriteAllText(path, "stub");
        _vm.Open(path);
        Assert.Equal(path, _vm.CurrentPath);
        Assert.Equal(TimeSpan.Zero, _vm.Position);
    }

    [Fact]
    public void MarkIn_SetsInPointToCurrentPosition()
    {
        _playback.Open("video.mp4");
        _playback.Seek(TimeSpan.FromSeconds(42));
        _vm.MarkInCommand.Execute(null);
        Assert.Equal(TimeSpan.FromSeconds(42), _vm.InPoint);
        Assert.True(_vm.HasMarkedIn);
    }

    [Fact]
    public void MarkOut_SetsOutPointToCurrentPosition()
    {
        _playback.Open("video.mp4");
        _playback.Seek(TimeSpan.FromSeconds(60));
        _vm.MarkOutCommand.Execute(null);
        Assert.Equal(TimeSpan.FromSeconds(60), _vm.OutPoint);
        Assert.True(_vm.HasMarkedOut);
    }

    [Fact]
    public void ClearInOut_ResetsBothPoints()
    {
        _playback.Open("video.mp4");
        _playback.Seek(TimeSpan.FromSeconds(10));
        _vm.MarkInCommand.Execute(null);
        _vm.MarkOutCommand.Execute(null);
        _vm.ClearInOutCommand.Execute(null);
        Assert.Null(_vm.InPoint);
        Assert.Null(_vm.OutPoint);
        Assert.False(_vm.HasMarkedIn);
        Assert.False(_vm.HasMarkedOut);
    }

    [Fact]
    public void HasCompleteClip_TrueWhenBothSetAndOutAfterIn()
    {
        _playback.Open("video.mp4");
        _playback.Seek(TimeSpan.FromSeconds(10));
        _vm.MarkInCommand.Execute(null);
        _playback.Seek(TimeSpan.FromSeconds(20));
        _vm.MarkOutCommand.Execute(null);
        Assert.True(_vm.HasCompleteClip);
    }

    [Fact]
    public void HasCompleteClip_FalseWhenOutBeforeIn()
    {
        _playback.Open("video.mp4");
        _playback.Seek(TimeSpan.FromSeconds(20));
        _vm.MarkInCommand.Execute(null);
        _playback.Seek(TimeSpan.FromSeconds(10));
        _vm.MarkOutCommand.Execute(null);
        Assert.False(_vm.HasCompleteClip);
    }

    [Fact]
    public void IncreaseRate_MovesToNextStepUp()
    {
        var initialRate = _vm.Rate;
        _vm.IncreaseRateCommand.Execute(null);
        Assert.True(_vm.Rate > initialRate);
    }

    [Fact]
    public void DecreaseRate_MovesToNextStepDown()
    {
        _vm.IncreaseRateCommand.Execute(null); // move to 1.25
        _vm.IncreaseRateCommand.Execute(null); // move to 1.5
        _vm.DecreaseRateCommand.Execute(null); // back to 1.25
        Assert.Equal(1.25, _vm.Rate);
    }

    [Fact]
    public void Rate_ClampsBetween025And8()
    {
        for (int i = 0; i < 20; i++)
            _vm.IncreaseRateCommand.Execute(null);
        Assert.Equal(8.0, _vm.Rate);

        for (int i = 0; i < 20; i++)
            _vm.DecreaseRateCommand.Execute(null);
        Assert.Equal(0.25, _vm.Rate);
    }

    [Fact]
    public void Rate_ClampsToConfiguredMaxRate()
    {
        // 上限 2.0：直接赋值和逐级增加都不得超过
        var vm = new PlayerViewModel(_playback, _screenshots, _clips, () => 5, () => 0.25, () => 2.0);
        vm.Rate = 16.0;
        Assert.Equal(2.0, vm.Rate);
        for (int i = 0; i < 10; i++)
            vm.IncreaseRateCommand.Execute(null);
        Assert.Equal(2.0, vm.Rate);
        Assert.True(vm.IsRateAtMax);

        // 可达倍速列表只含不超过上限的档位
        Assert.All(vm.AvailableRates, r => Assert.True(r.Value <= 2.0 + 0.001));
    }

    [Fact]
    public void Position_UpdatedFromPlaybackEvent()
    {
        _playback.Open("video.mp4");
        _playback.SimulatePositionChange(TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(30), _vm.Position);
    }

    [Fact]
    public void Position_SetterCallsPlaybackSeek()
    {
        // When we set Position directly (simulating timeline drag),
        // it should call _playback.Seek.
        _vm.Position = TimeSpan.FromSeconds(15);
        Assert.Equal(TimeSpan.FromSeconds(15), _playback.Position);
    }

    [Fact]
    public void PositionRatio_ComputesFromPositionAndLength()
    {
        _playback.Length = TimeSpan.FromSeconds(100);
        _vm.Open("video.mp4");
        _playback.Seek(TimeSpan.FromSeconds(50));
        Assert.Equal(0.5, _vm.PositionRatio, 2);
    }

    [Fact]
    public void LoopMode_ToggleChangesState()
    {
        Assert.False(_vm.LoopMode);
        _vm.ToggleLoopCommand.Execute(null);
        Assert.True(_vm.LoopMode);
    }

    [Fact]
    public async Task CaptureScreenshot_RaisesScreenshotCapturedEvent()
    {
        // v0.20.0 起截图为 ffmpeg 抽帧：需要真实 ffmpeg（SkipIfMissing）
        var ffmpegPath = FindBundledFfmpeg();
        if (ffmpegPath is null) return;
        var source = TryCreateTestVideo(ffmpegPath, Path.Combine(_tempDir, "src-cap.mp4"));
        if (source is null) return;

        var runner = new FfmpegRunner(ffmpegPath,
            Path.Combine(Path.GetDirectoryName(ffmpegPath)!, "ffprobe.exe"));
        var shotsDir = Path.Combine(_tempDir, "shots-cap");
        Directory.CreateDirectory(shotsDir);
        var stub = new StubVideoPlayback { Length = TimeSpan.FromSeconds(2) };
        stub.Open(source);
        stub.Seek(TimeSpan.FromSeconds(1));
        var svc = new ScreenshotService(stub, runner, shotsDir);
        using var vm = new PlayerViewModel(stub, svc, _clips, () => 5, () => 0.25, () => 8.0);

        ScreenshotEntry? captured = null;
        vm.ScreenshotCaptured += (_, e) => captured = e;

        await vm.CaptureScreenshotCommand.ExecuteAsync();
        Assert.NotNull(captured);
        Assert.True(File.Exists(captured!.OutputPath));
    }

    [Fact]
    public void TimeCodeDisplayString_NotEmpty_WhenPositionSet()
    {
        _playback.Open("video.mp4");
        _playback.Seek(TimeSpan.FromSeconds(5));
        Assert.False(string.IsNullOrEmpty(_vm.TimeCodeDisplayString));
    }

    [Fact]
    public void EndReached_WhenLoopMode_SeeksToStartAndPlays()
    {
        _playback.Open("video.mp4");
        _playback.Seek(TimeSpan.FromSeconds(100));
        _vm.ToggleLoopCommand.Execute(null); // enable loop
        _playback.SimulateEndReached();
        Assert.True(_playback.IsPlaying); // auto-restarted
    }

    // --- 裁切点批量重设（M5 导入/批量指令） ---

    [Fact]
    public void SetCutPoints_ReplacesCollectionSortedAndDeduped()
    {
        _vm.AddCutPointAt(TimeSpan.FromSeconds(10));
        // 乱序 + 列表内重复（SetCutPoints 先清空集合，重复点彼此去重）
        var points = new[]
        {
            TimeSpan.FromSeconds(260),
            TimeSpan.FromSeconds(90),
            TimeSpan.FromSeconds(260),
            TimeSpan.FromSeconds(10.05),
        };
        _vm.SetCutPoints(points);

        Assert.Equal(3, _vm.CutPoints.Count); // 列表内重复的 260s 只保留一次
        Assert.Equal(TimeSpan.FromSeconds(10.05), _vm.CutPoints[0]);
        Assert.Equal(TimeSpan.FromSeconds(90), _vm.CutPoints[1]);
        Assert.Equal(TimeSpan.FromSeconds(260), _vm.CutPoints[2]);
    }

    [Fact]
    public void AddCutPointAt_InsertsInOrder()
    {
        _vm.AddCutPointAt(TimeSpan.FromSeconds(30));
        _vm.AddCutPointAt(TimeSpan.FromSeconds(10));
        _vm.AddCutPointAt(TimeSpan.FromSeconds(20));
        Assert.Equal(
            new double[] { 10, 20, 30 },
            _vm.CutPoints.Select(c => c.TotalSeconds));
    }

    // --- 按值删除裁切点（笔记面板裁切点行删除用，W1-C2-D-06） ---

    [Fact]
    public void RemoveCutPoint_RemovesMatchingValue()
    {
        _vm.AddCutPointAt(TimeSpan.FromSeconds(10));
        _vm.AddCutPointAt(TimeSpan.FromSeconds(20));
        _vm.AddCutPointAt(TimeSpan.FromSeconds(30));

        var removed = _vm.RemoveCutPoint(TimeSpan.FromSeconds(20));

        Assert.True(removed);
        Assert.Equal(
            new double[] { 10, 30 },
            _vm.CutPoints.Select(c => c.TotalSeconds));
    }

    [Fact]
    public void RemoveCutPoint_NoMatch_ReturnsFalse()
    {
        _vm.AddCutPointAt(TimeSpan.FromSeconds(10));
        Assert.False(_vm.RemoveCutPoint(TimeSpan.FromSeconds(55)));
        Assert.Single(_vm.CutPoints);
    }

    [Fact]
    public void RemoveCutPoint_FallsBackToFullVideoSelection()
    {
        var path = Path.Combine(_tempDir, "video.mp4");
        File.WriteAllText(path, "stub");
        _vm.Open(path);
        _vm.AddCutPointAt(TimeSpan.FromSeconds(10));
        _vm.SelectSegmentAt(TimeSpan.FromSeconds(15));
        Assert.True(_vm.HasSelectedSegment);

        _vm.RemoveCutPoint(TimeSpan.FromSeconds(10));

        // 删除唯一裁切点后整片回退为片段 1（0 → 视频尾）
        Assert.True(_vm.HasSelectedSegment);
        Assert.Equal(TimeSpan.Zero, _vm.SelectedSegmentStart);
        Assert.Equal(TimeSpan.FromMinutes(3), _vm.SelectedSegmentEnd);
    }

    // --- 播放头自动选中片段 + X 键标记（v0.21.0）---

    [Fact]
    public void Playhead_AutoSelectsSegmentOnPositionChange()
    {
        var path = Path.Combine(_tempDir, "video.mp4");
        File.WriteAllText(path, "stub");
        _vm.Open(path);
        _vm.AddCutPointAt(TimeSpan.FromSeconds(60));

        _playback.Seek(TimeSpan.FromSeconds(30));
        Assert.True(_vm.HasSelectedSegment);
        Assert.Equal(TimeSpan.Zero, _vm.SelectedSegmentStart);
        Assert.Equal(TimeSpan.FromSeconds(60), _vm.SelectedSegmentEnd);

        _playback.Seek(TimeSpan.FromSeconds(90));
        Assert.Equal(TimeSpan.FromSeconds(60), _vm.SelectedSegmentStart);
        Assert.Equal(TimeSpan.FromMinutes(3), _vm.SelectedSegmentEnd);
    }

    [Fact]
    public void Playhead_NoCutPoints_SelectsFullVideo()
    {
        var path = Path.Combine(_tempDir, "video.mp4");
        File.WriteAllText(path, "stub");
        _vm.Open(path);
        _playback.Seek(TimeSpan.FromSeconds(30));
        // 无裁切点时整片视为片段 1：高亮跟随 + X 可标记
        Assert.True(_vm.HasSelectedSegment);
        Assert.Equal(TimeSpan.Zero, _vm.SelectedSegmentStart);
        Assert.Equal(TimeSpan.FromMinutes(3), _vm.SelectedSegmentEnd);
    }

    [Fact]
    public void ToggleMarkSelectedSegment_RaisesEventWithSegmentBounds()
    {
        var path = Path.Combine(_tempDir, "video.mp4");
        File.WriteAllText(path, "stub");
        _vm.Open(path);
        _vm.AddCutPointAt(TimeSpan.FromSeconds(60));
        _playback.Seek(TimeSpan.FromSeconds(30));

        (TimeSpan Start, TimeSpan End)? received = null;
        _vm.ToggleSegmentMarkRequested += (_, seg) => received = seg;

        Assert.True(_vm.ToggleMarkSelectedSegmentCommand.CanExecute(null));
        _vm.ToggleMarkSelectedSegmentCommand.Execute(null);

        Assert.NotNull(received);
        Assert.Equal(TimeSpan.Zero, received!.Value.Start);
        Assert.Equal(TimeSpan.FromSeconds(60), received.Value.End);
    }

    [Fact]
    public void ToggleMarkSelectedSegment_NoSelection_CannotExecuteAndNoEvent()
    {
        var raised = false;
        _vm.ToggleSegmentMarkRequested += (_, _) => raised = true;

        Assert.False(_vm.ToggleMarkSelectedSegmentCommand.CanExecute(null));
        _vm.ToggleMarkSelectedSegmentCommand.Execute(null);
        Assert.False(raised);
    }

    private static string? FindBundledFfmpeg()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "PianAssistant", "Tools", "ffmpeg", "ffmpeg.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>用 lavfi testsrc 生成测试视频；失败（不支持 lavfi/超时）返回 null。</summary>
    private static string? TryCreateTestVideo(string ffmpegPath, string outputPath)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-f lavfi -i testsrc=duration=2:size=320x240:rate=10 -y \"{outputPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(30000);
            return p is { HasExited: true, ExitCode: 0 } && File.Exists(outputPath) ? outputPath : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>软件级音量记忆（v0.19.0）：初始恢复 + 变化回调。</summary>
public class PlayerViewModelVolumeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StubVideoPlayback _playback;
    private readonly FfmpegRunner _ffmpeg;
    private readonly ScreenshotService _screenshots;
    private readonly ClipExtractionService _clips;

    public PlayerViewModelVolumeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-vol-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var ffmpegPath = Path.Combine(_tempDir, "ffmpeg.exe");
        var ffprobePath = Path.Combine(_tempDir, "ffprobe.exe");
        File.WriteAllText(ffmpegPath, "stub");
        File.WriteAllText(ffprobePath, "stub");
        _ffmpeg = new FfmpegRunner(ffmpegPath, ffprobePath);
        _playback = new StubVideoPlayback();
        _screenshots = new ScreenshotService(_playback, _ffmpeg, _tempDir, "png");
        _clips = new ClipExtractionService(_ffmpeg, _tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private PlayerViewModel Create(int initialVolume = 100, Action<int>? persisted = null) =>
        new(_playback, _screenshots, _clips, () => 5, () => 0.25, () => 8.0,
            initialVolume: initialVolume, volumePersisted: persisted);

    [Fact]
    public void InitialVolume_AppliedToPlayback_AndExposed()
    {
        using var vm = Create(initialVolume: 45);
        Assert.Equal(45, vm.Volume);
        Assert.Equal(45, _playback.Volume);
    }

    [Fact]
    public void VolumeChange_RaisesPersistedCallback_WithClampedValue()
    {
        var persisted = new List<int>();
        using var vm = Create(initialVolume: 80, persisted: v => persisted.Add(v));
        vm.Volume = 130; // 夹紧到 100
        Assert.Equal(100, vm.Volume);
        Assert.Equal(100, _playback.Volume);
        vm.Volume = -5;  // 夹紧到 0
        Assert.Equal(0, vm.Volume);
        Assert.Equal(0, _playback.Volume);
        Assert.Equal(new[] { 100, 0 }, persisted);
    }

    [Fact]
    public void VolumeChange_WithoutCallback_DoesNotThrow()
    {
        using var vm = Create(initialVolume: 100);
        vm.Volume = 30;
        Assert.Equal(30, vm.Volume);
    }

    [Fact]
    public void ClearCutPointsCommand_RaisesClearAllMarksRequested_InsteadOfDirectClear()
    {
        // v0.20.0：清空命令改为发确认请求（裁切点+截图标记），不在 Player 层直接清
        var raised = 0;
        using var vm = Create();
        vm.ClearAllMarksRequested += (_, _) => raised++;
        var path = Path.Combine(_tempDir, "clear.mp4");
        File.WriteAllText(path, "stub");
        vm.Open(path);
        vm.CutPoints.Add(TimeSpan.FromSeconds(5));
        vm.ClearCutPointsCommand.Execute(null);
        Assert.Equal(1, raised);
        Assert.Single(vm.CutPoints); // 未被直接清空，等待 Shell 确认后执行
    }
}
