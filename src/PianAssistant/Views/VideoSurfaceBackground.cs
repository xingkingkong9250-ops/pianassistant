using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PianAssistant.Views;

/// <summary>
/// 消除视频区上下「白边」（v1.7.8）。
/// 根因（实测）：LibVLCSharp.WPF 的 VideoHwndHost 用系统预定义 <c>Static</c> 类承载视频，
/// libvlc 再在其内部创建 <c>VLC video main</c> / <c>VLC video output</c> 子窗口。
/// VLC 按视频宽高比做信箱模式，上下留出的黑边区域没有被任何窗口真正绘制，
/// 露出 Static 类默认背景 COLOR_BTNFACE（#F0F0F0）→ 用户看到的「白边」。
/// 修复：对这些承载窗口做 WndProc 子类化，WM_ERASEBKGND 一律填黑，并同步改类背景刷兜底。
/// </summary>
internal static class VideoSurfaceBackground
{
    private const int WmEraseBkgnd = 0x0014;
    private const int WmPaint = 0x000F;
    private const int GclpHbrBackground = -10;
    private const int GwlWndProc = -4;
    private const int BlackBrushIndex = 4;
    private const uint RdwInvalidate = 0x0001;
    private const uint RdwErase = 0x0004;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0400;
    private const int WsClipChildren = 0x02000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr Hdc;
        public bool Erase;
        public WinRect RcPaint;
        public bool Restore;
        public bool IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect
    {
        public int Left, Top, Right, Bottom;
    }

    private delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out WinRect rect);
    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int index, IntPtr newValue);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newValue);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr CallWindowProc(IntPtr oldProc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr hWnd, IntPtr rect, IntPtr region, uint flags);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref WinRect rect, IntPtr brush);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int index);

    // 委托必须常驻静态字段，否则被 GC 回收会导致回调崩溃
    private static readonly EnumWindowProc EnumProc = OnChildWindow;
    private static readonly WndProcDelegate WndProcHandler = OnWndProc;
    private static readonly IntPtr BlackBrush = GetStockObject(BlackBrushIndex);
    private static readonly Dictionary<IntPtr, IntPtr> OldProcs = new();

    public static void Apply(Window owner, FrameworkElement videoView)
    {
        var mainHwnd = new WindowInteropHelper(owner).Handle;
        if (mainHwnd == IntPtr.Zero) return;

        var tl = videoView.PointToScreen(new Point(0, 0));
        var br = videoView.PointToScreen(new Point(videoView.ActualWidth, videoView.ActualHeight));
        _expectedWidth = (int)Math.Round(br.X - tl.X);
        _expectedHeight = (int)Math.Round(br.Y - tl.Y);
        if (_expectedWidth < 1 || _expectedHeight < 1) return;

        EnumChildWindows(mainHwnd, EnumProc, IntPtr.Zero);
    }

    public static void ApplyWithRetry(Window owner, FrameworkElement videoView)
    {
        Apply(owner, videoView);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Apply(owner, videoView);
        };
        timer.Start();
    }

    private static int _expectedWidth;
    private static int _expectedHeight;

    private static bool OnChildWindow(IntPtr hWnd, IntPtr lParam)
    {
        var sb = new StringBuilder(128);
        GetClassName(hWnd, sb, sb.Capacity);
        var cls = sb.ToString();

        var isSurfaceHost = cls.Equals("Static", StringComparison.Ordinal);
        var isVlcMain = cls.StartsWith("VLC video main", StringComparison.Ordinal);
        if (!isSurfaceHost && !isVlcMain) return true;

        if (!GetClientRect(hWnd, out var rc)) return true;
        // 只处理覆盖整个视频区的窗口，避免误伤其它 Static 控件
        if (Math.Abs(rc.Right - _expectedWidth) > 3 || Math.Abs(rc.Bottom - _expectedHeight) > 3) return true;

        ApplyToWindow(hWnd, cls);
        return true;
    }

    private static void ApplyToWindow(IntPtr hWnd, string cls)
    {
        if (!OldProcs.ContainsKey(hWnd))
        {
            var prev = SetWindowLongPtr(hWnd, GwlWndProc, Marshal.GetFunctionPointerForDelegate(WndProcHandler));
            if (prev != IntPtr.Zero)
            {
                OldProcs[hWnd] = prev;
                Services.AppLog.Info($"[VideoSurface] 子类化成功 hwnd=0x{hWnd.ToInt64():X} class='{cls}'");
            }
            else
            {
                Services.AppLog.Info($"[VideoSurface] 子类化失败 hwnd=0x{hWnd.ToInt64():X} class='{cls}' err={Marshal.GetLastWin32Error()}");
            }
        }

        // 只有带 WS_CLIPCHILDREN 的容器才能整体填黑（否则会盖住视频画面）
        var style = (int)GetWindowLongPtr(hWnd, -16).ToInt64();
        if ((style & WsClipChildren) != 0)
            PaintWindows.Add(hWnd);

        SetClassLongPtr(hWnd, GclpHbrBackground, BlackBrush);
        RedrawWindow(hWnd, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwErase | RdwAllChildren | RdwUpdateNow);
    }

    private static readonly HashSet<IntPtr> PaintWindows = new();

    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hWnd, out PaintStruct ps);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hWnd, ref PaintStruct ps);

    private static IntPtr OnWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmEraseBkgnd)
        {
            if (GetClientRect(hWnd, out var rc))
                FillRect(wParam, ref rc, BlackBrush);
            return new IntPtr(1);
        }
        if (msg == WmPaint && PaintWindows.Contains(hWnd))
        {
            var hdc = BeginPaint(hWnd, out var ps);
            if (GetClientRect(hWnd, out var rc))
                FillRect(hdc, ref rc, BlackBrush);
            EndPaint(hWnd, ref ps);
            return IntPtr.Zero;
        }
        if (OldProcs.TryGetValue(hWnd, out var oldProc))
            return CallWindowProc(oldProc, hWnd, msg, wParam, lParam);
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
