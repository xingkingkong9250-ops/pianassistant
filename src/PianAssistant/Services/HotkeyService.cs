using System.Globalization;
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
        var displayString = gesture.GetDisplayStringForCulture(CultureInfo.InvariantCulture);
        foreach (var kv in _active)
        {
            if (TryParseGesture(kv.Value, out var g) &&
                g.Key == gesture.Key &&
                g.Modifiers == gesture.Modifiers)
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
}
