using System.Windows.Input;
using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// Maps <see cref="HotkeyAction"/> → WPF KeyGesture string and back.
/// Holds the default (DaVinci Resolve inspired) keymap and the user's
/// custom overrides (persisted via <see cref="AppSettingsService"/>).
/// </summary>
public sealed class HotkeyService
{
    /// <summary>
    /// Default action → gesture map (DaVinci Resolve inspired, M0 baseline).
    /// Gesture strings are in WPF KeyGesture format (e.g. "Space", "Shift+Left",
    /// "Ctrl+O"). User can override each in W3 settings.
    /// </summary>
    public static readonly IReadOnlyDictionary<HotkeyAction, string> Defaults =
        new Dictionary<HotkeyAction, string>
        {
            [HotkeyAction.PlayPause] = "Space",
            [HotkeyAction.NextFrame] = "Right",
            [HotkeyAction.PreviousFrame] = "Left",
            [HotkeyAction.SeekBackward1Second] = "Shift+Left",
            [HotkeyAction.SeekForward1Second] = "Shift+Right",
            [HotkeyAction.SeekBackward10Seconds] = "Ctrl+Left",
            [HotkeyAction.SeekForward10Seconds] = "Ctrl+Right",
            [HotkeyAction.SeekToStart] = "Home",
            [HotkeyAction.SeekToEnd] = "End",
            [HotkeyAction.IncreaseRate] = "OemPlus",
            [HotkeyAction.DecreaseRate] = "OemMinus",
            [HotkeyAction.MarkIn] = "I",
            [HotkeyAction.MarkOut] = "O",
            [HotkeyAction.ClearInOut] = "Ctrl+Shift+I",
            [HotkeyAction.CaptureScreenshot] = "S",
            [HotkeyAction.ExtractClip] = "Ctrl+Shift+E",
            [HotkeyAction.AddCutPoint] = "C",
            [HotkeyAction.RemoveLastCutPoint] = "Back",
            [HotkeyAction.ClearCutPoints] = "Ctrl+Shift+C",
            [HotkeyAction.ExportSelectedSegment] = "Ctrl+E",
            [HotkeyAction.MarkSelectedSegment] = "X",
            [HotkeyAction.ToggleFullscreen] = "F",
            [HotkeyAction.ExitFullscreen] = "Esc",
            [HotkeyAction.TogglePlayPauseLoop] = "L",
            [HotkeyAction.SeekPercent10] = "D1",
            [HotkeyAction.SeekPercent20] = "D2",
            [HotkeyAction.SeekPercent30] = "D3",
            [HotkeyAction.SeekPercent40] = "D4",
            [HotkeyAction.SeekPercent50] = "D5",
            [HotkeyAction.SeekPercent60] = "D6",
            [HotkeyAction.SeekPercent70] = "D7",
            [HotkeyAction.SeekPercent80] = "D8",
            [HotkeyAction.SeekPercent90] = "D9",
            [HotkeyAction.ToggleSidebarPanel] = "Tab",
            [HotkeyAction.ToggleMute] = "M",
            [HotkeyAction.VolumeUp] = "Ctrl+Up",
            [HotkeyAction.VolumeDown] = "Ctrl+Down",
            [HotkeyAction.PreviousNote] = "OemOpenBrackets",
            [HotkeyAction.NextNote] = "OemCloseBrackets",
        };

    private readonly Dictionary<HotkeyAction, string> _active;

    public HotkeyService()
    {
        _active = new Dictionary<HotkeyAction, string>(Defaults);
    }

    /// <summary>Current action → gesture map (defaults + user overrides).</summary>
    public IReadOnlyDictionary<HotkeyAction, string> Active => _active;

    /// <summary>Apply a user override to the active map.</summary>
    public void SetCustom(HotkeyAction action, string gesture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gesture);
        _active[action] = gesture;
    }

    /// <summary>Reset a single action to its default gesture.</summary>
    public void ResetToDefault(HotkeyAction action) =>
        _active[action] = Defaults[action];

    /// <summary>Reset all actions to defaults.</summary>
    public void ResetAll()
    {
        _active.Clear();
        foreach (var kv in Defaults) _active[kv.Key] = kv.Value;
    }

    /// <summary>Find the action mapped to a gesture, if any.</summary>
    public HotkeyAction? ResolveAction(KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        foreach (var kv in _active)
        {
            if (TryParseKeyModifiers(kv.Value, out var key, out var modifiers) &&
                key == gesture.Key &&
                modifiers == gesture.Modifiers)
            {
                return kv.Key;
            }
        }
        return null;
    }

    /// <summary>Parse a gesture string via <see cref="KeyGestureConverter"/>.</summary>
    public static bool TryParseGesture(string text, out KeyGesture gesture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        try
        {
            gesture = (KeyGesture)new KeyGestureConverter().ConvertFromInvariantString(text)!;
            return true;
        }
        catch (Exception)
        {
            gesture = new KeyGesture(Key.None);
            return false;
        }
    }

    /// <summary>
    /// 解析手势字符串为 (Key, ModifierKeys)。相比 <see cref="KeyGestureConverter"/>，
    /// 本方法额外支持「无修饰键的裸字母/数字键」（如 S、F、D1~D9）——WPF 的
    /// KeyGesture 构造器/转换器会以 "key and modifier combination is not supported"
    /// 拒绝它们，但 <see cref="KeyBinding"/> 直接赋值 Key/Modifiers 不受该限制，
    /// 因此运行时重建 InputBindings 必须走本方法。
    /// </summary>
    public static bool TryParseKeyModifiers(string text, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            var tokens = text.Trim().Split('+', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return false;
            // 末段是键名，其余是修饰键（可多个，如 Ctrl+Shift+E）
            key = (Key)new KeyConverter().ConvertFromInvariantString(tokens[^1].Trim())!;
            if (key == Key.None) return false;
            for (var i = 0; i < tokens.Length - 1; i++)
                modifiers |= (ModifierKeys)new ModifierKeysConverter()
                    .ConvertFromInvariantString(tokens[i].Trim())!;
            return true;
        }
        catch (Exception)
        {
            key = Key.None;
            modifiers = ModifierKeys.None;
            return false;
        }
    }
}
