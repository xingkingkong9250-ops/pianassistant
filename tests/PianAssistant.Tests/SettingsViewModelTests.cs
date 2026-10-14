using System.IO;
using PianAssistant.Services;
using PianAssistant.ViewModels;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// 设置窗新增项测试（M6 C3）：后退/快进步长（1~600 帧）与倍速步长（0.05~1.0）的夹紧与持久化。
/// </summary>
public class SettingsViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-settings-vm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private SettingsViewModel CreateVm(out AppSettingsService settings)
    {
        settings = new AppSettingsService(_tempDir);
        return new SettingsViewModel(settings, new HotkeyService());
    }

    [Fact]
    public void SeekStepFrames_ClampsLowTo1()
    {
        var vm = CreateVm(out _);
        vm.SeekStepFrames = 0;
        Assert.Equal(1, vm.SeekStepFrames);
    }

    [Fact]
    public void SeekStepFrames_ClampsHighTo600()
    {
        var vm = CreateVm(out _);
        vm.SeekStepFrames = 9999;
        Assert.Equal(600, vm.SeekStepFrames);
    }

    [Fact]
    public void RateStep_ClampsLowTo005()
    {
        var vm = CreateVm(out _);
        vm.RateStep = 0.001;
        Assert.Equal(0.05, vm.RateStep, 3);
    }

    [Fact]
    public void RateStep_ClampsHighTo1()
    {
        var vm = CreateVm(out _);
        vm.RateStep = 5.0;
        Assert.Equal(1.0, vm.RateStep, 3);
    }

    [Fact]
    public void Save_PersistsStepValues()
    {
        var vm = CreateVm(out _);
        vm.SeekStepFrames = 30;
        vm.RateStep = 0.5;
        vm.SaveCommand.Execute(null);

        var reloaded = new AppSettingsService(_tempDir);
        Assert.Equal(30, reloaded.SeekStepFrames);
        Assert.Equal(0.5, reloaded.RateStep, 3);
    }

    [Fact]
    public void Defaults_AreStepFrames5AndRateStep025()
    {
        var vm = CreateVm(out _);
        Assert.Equal(5, vm.SeekStepFrames);
        Assert.Equal(0.25, vm.RateStep, 3);
    }
}
