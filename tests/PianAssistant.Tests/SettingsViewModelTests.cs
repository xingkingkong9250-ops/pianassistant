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

    // --- M7：数据路径与恢复开关 ---

    [Fact]
    public void DataRoot_InitializedFromService()
    {
        var vm = CreateVm(out var settings);
        Assert.Equal(settings.DataRoot, vm.DataRoot);
    }

    [Fact]
    public void ValidateDataRoot_Blank_ReturnsNull()
    {
        var vm = CreateVm(out _);
        vm.DataRoot = "   ";
        Assert.Null(vm.ValidateDataRoot());
    }

    [Fact]
    public void Save_WithChangedDataRoot_RaisesEventAndPersists()
    {
        var vm = CreateVm(out _);
        var raised = false;
        vm.DataRootChanged += (_, _) => raised = true;

        var newRoot = Path.Combine(_tempDir, "customroot");
        vm.DataRoot = newRoot;
        vm.SaveCommand.Execute(null);

        Assert.True(raised);
        var reloaded = new AppSettingsService(_tempDir);
        Assert.Equal(newRoot, reloaded.DataRoot);
    }

    [Fact]
    public void Save_WithUnchangedDataRoot_DoesNotRaiseEvent()
    {
        var vm = CreateVm(out var settings);
        var raised = false;
        vm.DataRootChanged += (_, _) => raised = true;

        vm.DataRoot = settings.DataRoot;
        vm.SaveCommand.Execute(null);

        Assert.False(raised);
    }

    [Fact]
    public void Save_WithInvalidDataRoot_RaisesValidationFailedAndBlocks()
    {
        var vm = CreateVm(out _);
        string? error = null;
        var saved = false;
        vm.ValidationFailed += (_, msg) => error = msg;
        vm.Saved += (_, _) => saved = true;

        vm.DataRoot = "C:\\bad\0path"; // 含非法字符（NUL）
        vm.SaveCommand.Execute(null);

        Assert.NotNull(error);
        Assert.False(saved); // 校验失败时阻断保存
        var reloaded = new AppSettingsService(_tempDir);
        Assert.Null(reloaded.Root.DataRoot); // 未写回
    }

    [Fact]
    public void Save_PersistsRestoreToggles()
    {
        var vm = CreateVm(out _);
        vm.RestoreSession = false;
        vm.RestorePlaybackPosition = false;
        vm.SaveCommand.Execute(null);

        var reloaded = new AppSettingsService(_tempDir);
        Assert.False(reloaded.RestoreSession);
        Assert.False(reloaded.RestorePlaybackPosition);
    }
}
