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
    AddCutPoint,
    RemoveLastCutPoint,
    ClearCutPoints,
    ExportSelectedSegment,
    MarkSelectedSegment, // V: 标记/取消标记当前选中片段（v1.1.3 起默认 V，v0.21.0~v1.1.2 为 X）
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
    ToggleSidebarPanel, // 切换右侧边栏 笔记/片段导出 面板
    ToggleMute,         // M: 静音开关（M6 补齐：原 XAML 静态绑定迁入动态映射）
    VolumeUp,           // Ctrl+Up: 音量 +10
    VolumeDown,         // Ctrl+Down: 音量 -10
    PreviousNote,       // [ 键：跳到上一个活动标记
    NextNote,           // ] 键：跳到下一个活动标记
    Undo,               // Ctrl+Z：撤回上一步笔记操作（v1.6.0）
}
