using System.Windows.Input;
using PianAssistant.Interop;
using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// 全局热键（Win32 RegisterHotKey）：应用失去焦点时仍可触发。
/// 设计决策：只注册「截图」一个全局热键（手势跟随应用内快捷键映射，默认 S），
/// <b>不</b>注册 Space 播放/暂停——全局 Space 会拦截其他所有应用
/// （浏览器/编辑器/聊天）的空格输入，干扰性过强；如需可在设置中扩展。
/// 窗口消息由 View 层（MainWindow）通过 HwndSource.AddHook 转发，
/// 本服务不引用任何 UI 类型（仅 IntPtr）。
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    /// <summary>热键 ID：截图。</summary>
    public const int IdScreenshot = 1;

    private IntPtr _hwnd;
    private bool _registered;

    /// <summary>全局截图热键触发（WndProc 回调线程触发，消费方需切回 UI 线程）。</summary>
    public event EventHandler? ScreenshotRequested;

    /// <summary>
    /// 注册全局截图热键。<paramref name="gesture"/> 为 WPF KeyGesture 字符串
    /// （与应用内快捷键映射一致，如 "S"）。重复调用会先注销旧注册。
    /// </summary>
    /// <returns>注册成功返回 true；手势无效、HWND 为空或被其他应用占用时返回 false。</returns>
    public bool TryRegisterScreenshot(IntPtr hwnd, string gesture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gesture);
        if (hwnd == IntPtr.Zero) return false;
        if (_registered)
            Unregister();

        if (!HotkeyService.TryParseKeyModifiers(gesture, out var key, out var m))
            return false;

        uint modifiers = 0;
        if (m.HasFlag(ModifierKeys.Control)) modifiers |= Win32Hotkey.MOD_CONTROL;
        if (m.HasFlag(ModifierKeys.Shift)) modifiers |= Win32Hotkey.MOD_SHIFT;
        if (m.HasFlag(ModifierKeys.Alt)) modifiers |= Win32Hotkey.MOD_ALT;
        if (m.HasFlag(ModifierKeys.Windows)) modifiers |= Win32Hotkey.MOD_WIN;

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!Win32Hotkey.RegisterHotKey(hwnd, IdScreenshot, modifiers | Win32Hotkey.MOD_NOREPEAT, vk))
            return false;

        _hwnd = hwnd;
        _registered = true;
        return true;
    }

    /// <summary>注销已注册的全局热键（未注册时为空操作）。</summary>
    public void Unregister()
    {
        if (!_registered) return;
        Win32Hotkey.UnregisterHotKey(_hwnd, IdScreenshot);
        _registered = false;
    }

    /// <summary>
    /// View 层 WndProc 收到 WM_HOTKEY 时调用。
    /// 返回 true 表示消息属于本服务并已处理。
    /// </summary>
    public bool HandleHotkey(IntPtr wParam)
    {
        if (wParam.ToInt64() != IdScreenshot) return false;
        ScreenshotRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Dispose() => Unregister();
}
