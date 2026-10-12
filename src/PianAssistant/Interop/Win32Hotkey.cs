using System.Runtime.InteropServices;

namespace PianAssistant.Interop;

/// <summary>
/// Win32 全局热键 P/Invoke。工程规范：所有 P/Invoke 隔离在 Interop/。
/// </summary>
internal static class Win32Hotkey
{
    /// <summary>热键消息（WPARAM 为热键 ID）。</summary>
    public const int WM_HOTKEY = 0x0312;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    /// <summary>禁止按住键重复触发（Windows 7+）。</summary>
    public const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
