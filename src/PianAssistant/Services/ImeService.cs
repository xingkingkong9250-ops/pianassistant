using System.Runtime.InteropServices;

namespace PianAssistant.Services;

/// <summary>
/// 输入法（IME）模式控制：中文输入法（如微软拼音）处于中文模式时，
/// WPF 收到的按键为 <c>Key.ImeProcessed</c> 而非原始字母键（S/C/F 等），
/// 导致单键快捷键失效。本服务把当前窗口的 IME 转换状态强制为英文
/// （字母数字）模式，接线点（MainWindow）：
/// - 启动时（SourceInitialized）与窗口激活时各强制一次；
/// - 键盘焦点不在文本输入控件内时再次强制（文本框内不打扰，可正常输入中文笔记）；
/// - 退出时恢复进入程序前的系统状态。
/// </summary>
internal static class ImeService
{
    private const uint WM_IME_CONTROL = 0x0283;
    private const IntPtr IMC_SETCONVERSIONMODE = 0x0002;
    // IME_CMODE_ALPHANUMERIC：英文（字母数字）模式
    private const uint IME_CMODE_ALPHANUMERIC = 0x0000;

    private static uint? _savedConversion;
    private static uint _savedSentence;

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("imm32.dll")]
    private static extern bool ImmGetConversionStatus(IntPtr hIMC, out uint conversion, out uint sentence);

    [DllImport("imm32.dll")]
    private static extern bool ImmSetConversionStatus(IntPtr hIMC, uint conversion, uint sentence);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>强制当前窗口 IME 为英文模式；首次调用时保存原状态供退出恢复。</summary>
    public static void ForceEnglish(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        // 首次调用记录原始转换状态（仅在窗口激活时能取到有效上下文，取不到则下次再存）
        if (_savedConversion is null)
        {
            var imc = ImmGetContext(hwnd);
            if (imc != IntPtr.Zero)
            {
                if (ImmGetConversionStatus(imc, out var conv, out var sent))
                {
                    _savedConversion = conv;
                    _savedSentence = sent;
                }
                ImmReleaseContext(hwnd, imc);
            }
        }

        // 方式一：WM_IME_CONTROL → 默认 IME 窗口（对微软拼音等 TSF 输入法的兼容层有效）
        var imeWnd = ImmGetDefaultIMEWnd(hwnd);
        if (imeWnd != IntPtr.Zero)
            SendMessage(imeWnd, WM_IME_CONTROL, IMC_SETCONVERSIONMODE, (IntPtr)IME_CMODE_ALPHANUMERIC);

        // 方式二：imm32 经典接口（兼容老式输入法，同时兜底）
        var ctx = ImmGetContext(hwnd);
        if (ctx != IntPtr.Zero)
        {
            ImmSetConversionStatus(ctx, IME_CMODE_ALPHANUMERIC, _savedSentence);
            ImmReleaseContext(hwnd, ctx);
        }
    }

    /// <summary>退出时恢复进入程序前的 IME 转换状态（未保存过则不动）。</summary>
    public static void RestoreSaved(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || _savedConversion is not uint conv) return;
        var ctx = ImmGetContext(hwnd);
        if (ctx != IntPtr.Zero)
        {
            ImmSetConversionStatus(ctx, conv, _savedSentence);
            ImmReleaseContext(hwnd, ctx);
        }
    }
}
