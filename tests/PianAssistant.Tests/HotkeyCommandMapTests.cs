using System.IO;
using PianAssistant.Helpers;
using PianAssistant.Models;
using PianAssistant.Services;
using PianAssistant.Tests.Stubs;
using PianAssistant.ViewModels;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// 热键一致性测试（M6）：HotkeyService.Defaults 中的每条动作都必须能在
/// <see cref="HotkeyCommandMap"/> 中解析到非 null 的命令（不得留"假快捷键"）。
/// </summary>
public class HotkeyCommandMapTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StubVideoPlayback _playback;
    private readonly PlayerViewModel _player;
    private readonly HotkeyCommandMap _map;

    public HotkeyCommandMapTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-hotmap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var ffmpegPath = Path.Combine(_tempDir, "ffmpeg.exe");
        var ffprobePath = Path.Combine(_tempDir, "ffprobe.exe");
        File.WriteAllText(ffmpegPath, "stub");
        File.WriteAllText(ffprobePath, "stub");
        var ffmpeg = new FfmpegRunner(ffmpegPath, ffprobePath);
        _playback = new StubVideoPlayback();
        _player = new PlayerViewModel(
            _playback,
            new ScreenshotService(_playback, ffmpeg, _tempDir, "png"),
            new ClipExtractionService(ffmpeg, _tempDir),
            () => 5, () => 0.25, () => 8.0);
        _map = new HotkeyCommandMap(
            _player,
            new RelayCommand(() => { }),
            new RelayCommand(() => { }),
            new RelayCommand(() => { }));
    }

    public void Dispose()
    {
        _player.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void EveryDefaultAction_ResolvesToNonNullCommand()
    {
        foreach (var action in HotkeyService.Defaults.Keys)
        {
            var command = _map.ResolveCommand(action.ToString());
            Assert.NotNull(command);
            Assert.NotNull(_map.Resolve(action));
        }
    }

    [Fact]
    public void ResolveCommand_UnknownActionName_ReturnsNull()
    {
        Assert.Null(_map.ResolveCommand("NoSuchAction"));
    }

    [Fact]
    public void ResolveCommand_ExtractClip_ReturnsExtractClipCommand()
    {
        Assert.Same(_player.ExtractClipCommand, _map.ResolveCommand("ExtractClip"));
    }

    [Fact]
    public void SeekPercent10_MapsToSeekPercentCommand_WithParameter()
    {
        var binding = _map.Resolve(HotkeyAction.SeekPercent10);
        Assert.NotNull(binding);
        Assert.Same(_player.SeekPercentCommand, binding!.Value.Command);
        Assert.Equal(0.10, Assert.IsType<double>(binding.Value.Parameter));
    }

    [Fact]
    public void SeekPercentCommand_SeeksToRatioOfLength()
    {
        // D1 跳转：参数 0.5 时应 seek 到总时长的一半
        _playback.Length = TimeSpan.FromSeconds(100);
        _player.SeekPercentCommand.Execute(0.5);
        Assert.Equal(50.0, _playback.Position.TotalSeconds, 1);
    }
}
