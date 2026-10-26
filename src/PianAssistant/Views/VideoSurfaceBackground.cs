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
    private const int WmLButtonUp = 0x0202;
    private const int GclpHbrBackground = -10;
    private const int GwlWndProc = -4;
    private const int BlackBrushIndex = 4;
    private const uint RdwInvalidate = 0x0001;
    private const uint RdwErase = 0x0004;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0400;
    private const int WsClipChildren = 0x02000000;
    private const uint EventObjectCreate = 0x8000;
    private const uint WineventOutofcontext = 0x0000;
    private const int ObjidWindow = 0;

    /// <summary>
    /// v1.15.0：VLC 视频窗口收到左键抬起时，由子类化 WndProc 向主窗口 PostMessage
    /// 此自定义消息（WM_APP 段），主窗口据此切换播放/暂停（点击画面快捷暂停）。
    /// </summary>
    public const int WmVideoSurfaceClick = 0x8000 + 0x42;

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
    private delegate void WinEventDelegate(
        IntPtr hook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint thread, uint time);

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
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr mod, WinEventDelegate cb, uint processId, uint threadId, uint flags);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int index);

    // 委托必须常驻静态字段，否则被 GC 回收会导致回调崩溃
    private static readonly EnumWindowProc EnumProc = OnChildWindow;
    private static readonly WndProcDelegate WndProcHandler = OnWndProc;
    private static readonly WinEventDelegate WinEventHandler = OnWinEvent;
    private static readonly IntPtr BlackBrush = GetStockObject(BlackBrushIndex);
    private static readonly Dictionary<IntPtr, IntPtr> OldProcs = new();

    // v1.15.0：主窗口句柄（点击视频画面 → PostMessage 通知播放/暂停）
    private static IntPtr _notifyHwnd;
    // v1.15.0：已接管的 VLC 视频画面窗口（点击转发 + 即时接管判定）
    private static readonly HashSet<IntPtr> ClickableWindows = new();
    private static IntPtr _eventHook;

    public static void Apply(Window owner, FrameworkElement videoView)
    {
        var mainHwnd = new WindowInteropHelper(owner).Handle;
        if (mainHwnd == IntPtr.Zero) return;
        _notifyHwnd = mainHwnd; // v1.15.0：点击视频画面通知目标

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
        // v1.15.0：挂进程级 WinEvent 钩子——VLC vout 窗口「创建瞬间」即接管类背景刷，
        // 抢在首帧绘制前填黑，堵住三档重试之间偶尔闪白边的窗口期
        EnsureEventHook();
        // VLC 的 vout 窗口创建时机不稳定（常在 Playing 事件之后数百毫秒），
        // 单次 Apply 容易扑空 → 分三档延迟补接管（v1.14.0 由单档 800ms 加强）
        foreach (var delay in new[] { 250, 800, 2000 })
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Apply(owner, videoView);
            };
            timer.Start();
        }
    }

    /// <summary>
    /// v1.15.0：进程内窗口创建事件钩子（out-of-context，经 UI 线程消息循环派发）。
    /// 只接管 VLC 自建的视频窗口（进程内仅播放器会创建）；Static 宿主由枚举路径处理。
    /// </summary>
    private static void EnsureEventHook()
    {
        if (_eventHook != IntPtr.Zero) return;
        _eventHook = SetWinEventHook(
            EventObjectCreate, EventObjectCreate, IntPtr.Zero,
            WinEventHandler, GetCurrentProcessId(), 0, WineventOutofcontext);
    }

    private static void OnWinEvent(
        IntPtr hook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != ObjidWindow || hWnd == IntPtr.Zero) return;
        if (OldProcs.ContainsKey(hWnd)) return; // 已接管
        var sb = new StringBuilder(128);
        GetClassName(hWnd, sb, sb.Capacity);
        // 「VLC video main」与其渲染子窗口「VLC video output」都接管：
        // 前者填黑兜底，后者是实际受击面（点击转发依赖子类化）
        if (sb.ToString().StartsWith("VLC video", StringComparison.Ordinal))
            ApplyToWindow(hWnd, sb.ToString());
    }

    private static int _expectedWidth;
    private static int _expectedHeight;

    private static bool OnChildWindow(IntPtr hWnd, IntPtr lParam)
    {
        var sb = new StringBuilder(128);
        GetClassName(hWnd, sb, sb.Capacity);
        var cls = sb.ToString();

        var isSurfaceHost = cls.Equals("Static", StringComparison.Ordinal);
        // v1.15.0：「VLC video main」+ 渲染子窗口「VLC video output」统一按前缀接管
        var isVlcMain = cls.StartsWith("VLC video", StringComparison.Ordinal);
        if (!isSurfaceHost && !isVlcMain) return true;

        // VLC 自建视频窗口进程内只有播放器会创建，一律接管；
        // Static 可能是其它控件宿主，需尺寸匹配确认是视频区（v1.14.0 放宽 VLC 类尺寸校验）
        if (isSurfaceHost)
        {
            if (!GetClientRect(hWnd, out var rc)) return true;
            if (Math.Abs(rc.Right - _expectedWidth) > 3 || Math.Abs(rc.Bottom - _expectedHeight) > 3) return true;
        }

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

        // v1.15.0：VLC 视频画面窗口登记为「点击 → 播放/暂停」转发源
        if (cls.StartsWith("VLC video", StringComparison.Ordinal))
            ClickableWindows.Add(hWnd);

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
        // v1.15.0：点击视频画面 → 通知主窗口切换播放/暂停（消息不吞，照常透传给 VLC）
        if (msg == WmLButtonUp && ClickableWindows.Contains(hWnd) && _notifyHwnd != IntPtr.Zero)
            PostMessage(_notifyHwnd, WmVideoSurfaceClick, IntPtr.Zero, IntPtr.Zero);
        if (OldProcs.TryGetValue(hWnd, out var oldProc))
            return CallWindowProc(oldProc, hWnd, msg, wParam, lParam);
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
