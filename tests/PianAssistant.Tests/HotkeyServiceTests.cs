using System.Windows.Input;
using PianAssistant.Models;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

public class HotkeyServiceTests
{
    [Fact]
    public void Defaults_HaveAll33Actions()
    {
        Assert.Equal(33, HotkeyService.Defaults.Count);
    }

    [Fact]
    public void Defaults_PlayPauseIsSpace()
    {
        Assert.Equal("Space", HotkeyService.Defaults[HotkeyAction.PlayPause]);
    }

    [Fact]
    public void Defaults_MarkInIsI()
    {
        Assert.Equal("I", HotkeyService.Defaults[HotkeyAction.MarkIn]);
    }

    [Fact]
    public void Defaults_MarkOutIsO()
    {
        Assert.Equal("O", HotkeyService.Defaults[HotkeyAction.MarkOut]);
    }

    [Fact]
    public void Defaults_ScreenshotIsS()
    {
        Assert.Equal("S", HotkeyService.Defaults[HotkeyAction.CaptureScreenshot]);
    }

    [Fact]
    public void Defaults_ExtractClipIsCtrlShiftE()
    {
        Assert.Equal("Ctrl+Shift+E", HotkeyService.Defaults[HotkeyAction.ExtractClip]);
    }

    [Fact]
    public void Defaults_AddCutPointIsC()
    {
        Assert.Equal("C", HotkeyService.Defaults[HotkeyAction.AddCutPoint]);
    }

    [Fact]
    public void Defaults_RemoveLastCutPointIsBack()
    {
        Assert.Equal("Back", HotkeyService.Defaults[HotkeyAction.RemoveLastCutPoint]);
    }

    [Fact]
    public void Defaults_ClearCutPointsIsCtrlShiftC()
    {
        Assert.Equal("Ctrl+Shift+C", HotkeyService.Defaults[HotkeyAction.ClearCutPoints]);
    }

    [Fact]
    public void Defaults_ExportSelectedSegmentIsCtrlE()
    {
        Assert.Equal("Ctrl+E", HotkeyService.Defaults[HotkeyAction.ExportSelectedSegment]);
    }

    [Fact]
    public void SetCustom_OverridesActive()
    {
        var svc = new HotkeyService();
        svc.SetCustom(HotkeyAction.PlayPause, "Ctrl+P");
        Assert.Equal("Ctrl+P", svc.Active[HotkeyAction.PlayPause]);
    }

    [Fact]
    public void ResetToDefault_RestoresSingleAction()
    {
        var svc = new HotkeyService();
        svc.SetCustom(HotkeyAction.PlayPause, "Ctrl+P");
        svc.ResetToDefault(HotkeyAction.PlayPause);
        Assert.Equal("Space", svc.Active[HotkeyAction.PlayPause]);
    }

    [Fact]
    public void ResetAll_RestoresAllActions()
    {
        var svc = new HotkeyService();
        svc.SetCustom(HotkeyAction.PlayPause, "Ctrl+P");
        svc.SetCustom(HotkeyAction.MarkIn, "Ctrl+I");
        svc.ResetAll();
        Assert.Equal("Space", svc.Active[HotkeyAction.PlayPause]);
        Assert.Equal("I", svc.Active[HotkeyAction.MarkIn]);
    }

    [Fact]
    public void ResolveAction_ReturnsMappedAction()
    {
        var svc = new HotkeyService();
        Assert.True(HotkeyService.TryParseGesture("Space", out var gesture));
        var action = svc.ResolveAction(gesture);
        Assert.Equal(HotkeyAction.PlayPause, action);
    }

    [Fact]
    public void ResolveAction_ReturnsNullForUnmappedGesture()
    {
        var svc = new HotkeyService();
        Assert.True(HotkeyService.TryParseGesture("F12", out var gesture));
        var action = svc.ResolveAction(gesture);
        Assert.Null(action);
    }

    [Fact]
    public void TryParseGesture_ValidGesture_ReturnsTrue()
    {
        Assert.True(HotkeyService.TryParseGesture("Ctrl+S", out _));
    }

    [Fact]
    public void TryParseGesture_InvalidGesture_ReturnsFalse()
    {
        Assert.False(HotkeyService.TryParseGesture("$$$", out _));
    }
}
