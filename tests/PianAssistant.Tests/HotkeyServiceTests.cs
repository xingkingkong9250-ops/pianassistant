using System.Windows.Input;
using PianAssistant.Models;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

public class HotkeyServiceTests
{
    [Fact]
    public void Defaults_HaveAll38Actions()
    {
        Assert.Equal(38, HotkeyService.Defaults.Count);
    }

    [Fact]
    public void Defaults_CoversEveryEnumValue()
    {
        // 每个 HotkeyAction 枚举值都必须在 Defaults 中有默认手势（不得留未定义动作）。
        foreach (HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))
            Assert.True(HotkeyService.Defaults.ContainsKey(action), $"{action} 缺少默认手势");
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

    // --- M6 新增键解析 ---

    [Fact]
    public void TryParseGesture_CtrlShiftE_ReturnsTrue()
    {
        Assert.True(HotkeyService.TryParseGesture("Ctrl+Shift+E", out var gesture));
        Assert.Equal(Key.E, gesture.Key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, gesture.Modifiers);
    }

    [Fact]
    public void TryParseKeyModifiers_D1_ReturnsD1Key()
    {
        // 裸数字键 D1：WPF KeyGesture 会拒绝，必须走 TryParseKeyModifiers
        Assert.True(HotkeyService.TryParseKeyModifiers("D1", out var key, out var modifiers));
        Assert.Equal(Key.D1, key);
        Assert.Equal(ModifierKeys.None, modifiers);
    }

    [Fact]
    public void TryParseKeyModifiers_BareLetter_ReturnsKey()
    {
        Assert.True(HotkeyService.TryParseKeyModifiers("S", out var key, out _));
        Assert.Equal(Key.S, key);
    }

    [Fact]
    public void TryParseKeyModifiers_CtrlShiftE_ReturnsCombinedModifiers()
    {
        Assert.True(HotkeyService.TryParseKeyModifiers("Ctrl+Shift+E", out var key, out var modifiers));
        Assert.Equal(Key.E, key);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, modifiers);
    }

    [Fact]
    public void TryParseKeyModifiers_AllDefaultGestures_ReturnTrue()
    {
        // 运行时重建 InputBindings 的硬约束：默认手势表中每条都必须可解析。
        foreach (var kv in HotkeyService.Defaults)
            Assert.True(HotkeyService.TryParseKeyModifiers(kv.Value, out _, out _),
                $"{kv.Key} 的默认手势无法解析：{kv.Value}");
    }

    [Fact]
    public void TryParseKeyModifiers_InvalidGesture_ReturnsFalse()
    {
        Assert.False(HotkeyService.TryParseKeyModifiers("$$$", out _, out _));
    }

    [Fact]
    public void TryParseGesture_NewModifierKeys_ReturnTrue()
    {
        Assert.True(HotkeyService.TryParseGesture("Ctrl+Up", out var up));
        Assert.Equal(Key.Up, up.Key);
        Assert.True(HotkeyService.TryParseGesture("OemOpenBrackets", out var bracket));
        Assert.Equal(Key.OemOpenBrackets, bracket.Key);
    }
}
