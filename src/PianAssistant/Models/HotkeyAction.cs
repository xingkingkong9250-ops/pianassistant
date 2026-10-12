namespace PianAssistant.Models;

/// <summary>
/// A hotkey action that can be triggered from the keyboard.
/// </summary>
public enum HotkeyAction
{
    PlayPause,
    NextFrame,
    PreviousFrame,
    SeekBackward1Second,
    SeekForward1Second,
    SeekBackward10Seconds,
    SeekForward10Seconds,
    SeekToStart,
    SeekToEnd,
    IncreaseRate,
    DecreaseRate,
    MarkIn,
    MarkOut,
    ClearInOut,
    CaptureScreenshot,
    ExtractClip,
    ToggleFullscreen,
    ExitFullscreen,
    TogglePlayPauseLoop, // L: A/B-like loop toggle (M2+)
    SeekPercent10,
    SeekPercent20,
    SeekPercent30,
    SeekPercent40,
    SeekPercent50,
    SeekPercent60,
    SeekPercent70,
    SeekPercent80,
    SeekPercent90,
    ToggleSidebarPanel, // switch between playlist/notes
}
