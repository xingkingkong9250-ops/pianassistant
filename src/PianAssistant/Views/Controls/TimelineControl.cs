using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PianAssistant.Models;

namespace PianAssistant.Views.Controls;

/// <summary>时间轴标记拖拽事件参数（v1.7.0）。</summary>
public sealed class MarkerDragEventArgs : EventArgs
{
    public MarkerDragEventArgs(TimeSpan originalPosition, TimeSpan newPosition)
    {
        OriginalPosition = originalPosition;
        NewPosition = newPosition;
    }

    /// <summary>拖拽前标记位置。</summary>
    public TimeSpan OriginalPosition { get; }

    /// <summary>松手时标记位置。</summary>
    public TimeSpan NewPosition { get; }
}

/// <summary>
/// 时间轴控件：可缩放、PR 风格播放头（白色竖线+顶部三角）、I/O 入出点、选中段。
/// 鼠标滚轮缩放，点击/拖动 seek，释放时触发段选中。
/// v1.7.0：裁切点/截图标记支持选中拖拽修改（虚影 + 实时 seek 预览，松手提交）。
/// v1.8.0：拆为三条并排轴——时间滑块轴（刻度/播放头/seek）、裁切片段轴（裁切点/
/// 入出点/选中段/已标记片段）、截图标记轴（截图色块）；各轴命中互不干扰。
/// </summary>
public sealed class TimelineControl : Control
{
    /// <summary>轴1 时间滑块轴高度：刻度、时间标签、播放头三角。</summary>
    public const double RulerAxisHeight = 20;

    /// <summary>轴2 裁切片段轴高度：裁切点、入出点、选中段、已标记片段。</summary>
    public const double ClipAxisHeight = 18;

    /// <summary>轴3 截图标记轴高度：截图色块。</summary>
    public const double MarkerAxisHeight = 18;

    /// <summary>三轴总高度。</summary>
    public const double TotalHeight = RulerAxisHeight + ClipAxisHeight + MarkerAxisHeight;

    // 轴边界（自上而下）
    private const double ClipAxisTop = RulerAxisHeight;
    private const double MarkerAxisTop = RulerAxisHeight + ClipAxisHeight;

    static TimelineControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(TimelineControl),
            new FrameworkPropertyMetadata(typeof(TimelineControl)));
    }

    // --- 依赖属性 ---

    public static readonly DependencyProperty PositionRatioProperty =
        DependencyProperty.Register(
            nameof(PositionRatio), typeof(double), typeof(TimelineControl),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LengthProperty =
        DependencyProperty.Register(
            nameof(Length), typeof(TimeSpan), typeof(TimelineControl),
            new FrameworkPropertyMetadata(TimeSpan.Zero,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty InPointProperty =
        DependencyProperty.Register(
            nameof(InPoint), typeof(TimeSpan?), typeof(TimelineControl),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OutPointProperty =
        DependencyProperty.Register(
            nameof(OutPoint), typeof(TimeSpan?), typeof(TimelineControl),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CutPointsProperty =
        DependencyProperty.Register(
            nameof(CutPoints), typeof(IEnumerable<TimeSpan>), typeof(TimelineControl),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedSegmentStartProperty =
        DependencyProperty.Register(
            nameof(SelectedSegmentStart), typeof(TimeSpan?), typeof(TimelineControl),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedSegmentEndProperty =
        DependencyProperty.Register(
            nameof(SelectedSegmentEnd), typeof(TimeSpan?), typeof(TimelineControl),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>笔记/截图/片段标记点（活动流同步生成），绘制为顶部小色块。</summary>
    public static readonly DependencyProperty MarkersProperty =
        DependencyProperty.Register(
            nameof(Markers), typeof(IEnumerable<TimelineMarker>), typeof(TimelineControl),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>已标记片段集合（笔记 ☑ 勾选同步），绘制为黄色半透明段带。</summary>
    public static readonly DependencyProperty MarkedSegmentsProperty =
        DependencyProperty.Register(
            nameof(MarkedSegments), typeof(IEnumerable<TimelineSegment>), typeof(TimelineControl),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender, OnMarkedSegmentsChanged));

    public static readonly DependencyProperty ScaleProperty =
        DependencyProperty.Register(
            nameof(Scale), typeof(double), typeof(TimelineControl),
            new FrameworkPropertyMetadata(1.0,
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnScaleChanged));

    // --- 属性 ---

    public double PositionRatio
    {
        get => (double)GetValue(PositionRatioProperty);
        set => SetValue(PositionRatioProperty, value);
    }

    public TimeSpan Length
    {
        get => (TimeSpan)GetValue(LengthProperty);
        set => SetValue(LengthProperty, value);
    }

    public TimeSpan? InPoint
    {
        get => (TimeSpan?)GetValue(InPointProperty);
        set => SetValue(InPointProperty, value);
    }

    public TimeSpan? OutPoint
    {
        get => (TimeSpan?)GetValue(OutPointProperty);
        set => SetValue(OutPointProperty, value);
    }

    public IEnumerable<TimeSpan> CutPoints
    {
        get => (IEnumerable<TimeSpan>)GetValue(CutPointsProperty);
        set => SetValue(CutPointsProperty, value);
    }

    public TimeSpan? SelectedSegmentStart
    {
        get => (TimeSpan?)GetValue(SelectedSegmentStartProperty);
        set => SetValue(SelectedSegmentStartProperty, value);
    }

    public TimeSpan? SelectedSegmentEnd
    {
        get => (TimeSpan?)GetValue(SelectedSegmentEndProperty);
        set => SetValue(SelectedSegmentEndProperty, value);
    }

    /// <summary>笔记/截图/片段标记点集合（模型层时间码）。</summary>
    public IEnumerable<TimelineMarker> Markers
    {
        get => (IEnumerable<TimelineMarker>)GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    /// <summary>已标记片段集合（黄 = ☑ 勾选）。</summary>
    public IEnumerable<TimelineSegment>? MarkedSegments
    {
        get => (IEnumerable<TimelineSegment>?)GetValue(MarkedSegmentsProperty);
        set => SetValue(MarkedSegmentsProperty, value);
    }

    // AffectsRender 仅在 DP 值（集合实例）替换时触发；集合内容变化需显式订阅重绘。
    private static void OnMarkedSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TimelineControl control) return;
        if (e.OldValue is INotifyCollectionChanged oldCol)
            oldCol.CollectionChanged -= control.OnMarkedSegmentsCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCol)
            newCol.CollectionChanged += control.OnMarkedSegmentsCollectionChanged;
        control.InvalidateTimelineVisual();
    }

    private void OnMarkedSegmentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => InvalidateTimelineVisual();

    // 集合同步可能来自非 UI 线程（VLC 事件链），InvalidateVisual 必须在 UI 线程执行。
    private void InvalidateTimelineVisual()
    {
        if (Dispatcher.CheckAccess())
            InvalidateVisual();
        else
            Dispatcher.BeginInvoke(() => InvalidateVisual());
    }

    /// <summary>缩放比例：1.0=适应宽度，越大越放大。范围 0.5~50。</summary>
    public double Scale
    {
        get => (double)GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, Math.Clamp(value, 0.5, 50.0));
    }

    // --- 事件 ---

    /// <summary>点击/拖动 seek，值为 0..1（基于完整时间轴比例）。</summary>
    public event EventHandler<double>? SeekRequested;

    /// <summary>点击裁切段时触发，值为点击位置的时间。</summary>
    public event EventHandler<TimeSpan>? SegmentSelected;

    /// <summary>缩放变化时触发，值为新的 Scale。</summary>
    public event EventHandler<double>? ScaleChanged;

    /// <summary>裁切点拖拽提交（原位置 → 新位置），由 Shell 落数据并纳入撤回（v1.7.0）。</summary>
    public event EventHandler<MarkerDragEventArgs>? CutPointDragged;

    /// <summary>截图标记拖拽提交（原位置 → 新位置），由 Shell 落数据并纳入撤回（v1.7.0）。</summary>
    public event EventHandler<MarkerDragEventArgs>? ScreenshotDragged;

    /// <summary>拖拽模式：普通 seek / 裁切点 / 截图标记。</summary>
    private enum DragMode { None, Seek, CutPoint, Screenshot }

    private DragMode _dragMode = DragMode.None;
    private TimeSpan _dragOriginal;   // 被拖标记原位置
    private TimeSpan _dragCurrent;    // 虚影当前位置
    private double _dragStartX;       // 按下时 X（判定是否构成拖拽）
    private bool _dragMoved;          // 位移 >3px 才算拖拽
    private const double HitTolerancePx = 6;
    private double _viewportWidth; // ScrollViewer 可见宽度
    // 标记颜色画笔缓存（ColorHex → 冻结画笔）
    private readonly Dictionary<string, SolidColorBrush> _markerBrushCache = new();

    /// <summary>按十六进制颜色取冻结画笔（带缓存与兜底）。</summary>
    private SolidColorBrush GetMarkerBrush(string colorHex)
    {
        if (_markerBrushCache.TryGetValue(colorHex, out var cached))
            return cached;
        SolidColorBrush brush;
        try
        {
            brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
        }
        catch (FormatException)
        {
            brush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
        }
        brush.Freeze();
        _markerBrushCache[colorHex] = brush;
        return brush;
    }

    private static void OnScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimelineControl tc)
        {
            // 更新控件宽度以适配缩放
            var baseWidth = tc._viewportWidth > 0 ? tc._viewportWidth : tc.ActualWidth;
            if (double.IsFinite(baseWidth) && baseWidth > 0 && double.IsFinite(tc.Scale))
                tc.Width = baseWidth * tc.Scale;
            tc.ScaleChanged?.Invoke(tc, tc.Scale);
        }
    }

    /// <summary>由外部 ScrollViewer 设置可见区域宽度。</summary>
    public void SetViewportWidth(double width)
    {
        // ScrollViewer.ViewportWidth 在布局未完成时可能为 NaN，必须拒绝（否则 Width/Measure 产生 NaN 引发布局异常风暴）
        if (!double.IsFinite(width) || width <= 0) return;
        _viewportWidth = width;
        Width = width * Scale;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // 兜底：任何非有限值（NaN/Infinity）一律回退为 0，确保 DesiredSize 永远有限
        var w = double.IsInfinity(availableSize.Width) ? _viewportWidth : availableSize.Width;
        if (!double.IsFinite(w) || w <= 0) w = 0;
        if (w > 0 && _viewportWidth <= 0)
            _viewportWidth = w;
        var desiredWidth = (_viewportWidth > 0 ? _viewportWidth : w) * Scale;
        if (!double.IsFinite(desiredWidth) || desiredWidth <= 0) desiredWidth = 0;
        return new Size(desiredWidth, TotalHeight);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var w = ActualWidth; // 缩放后的完整宽度
        var h = ActualHeight;
        var len = Length;
        var lenMs = len.TotalMilliseconds;

        // 三轴边界（v1.8.0）：轴1 时间滑块轴 / 轴2 裁切片段轴 / 轴3 截图标记轴
        var clipTop = ClipAxisTop;
        var clipBottom = MarkerAxisTop;
        var markerTop = MarkerAxisTop;
        var markerHeight = MarkerAxisHeight;

        // 画笔（色彩规范：裁切点红 #E51400 / 截图点绿 #4EC9B0 / 标记片段黄 #D7BA7D /
        // 选中段白 10% / I/O 蓝 #009AEC / 播放头白 —— 与笔记列表 AccentColor 对齐）
        var bg = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
        var clipLaneBg = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
        var lanePen = new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E)), 1);
        var tickPen = new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)), 1);
        var majorTickPen = new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1);
        var playheadPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)), 2);
        var inOutBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)) { Opacity = 0.25 };
        var inOutPen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x9A, 0xEC)), 1.5);
        var selectedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)) { Opacity = 0.10 };
        var selectedPen = new Pen(new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)), 1);
        var markedBrush = new SolidColorBrush(Color.FromRgb(0xD7, 0xBA, 0x7D)) { Opacity = 0.35 };
        var markedPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD7, 0xBA, 0x7D)), 1.5);
        var cutPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE5, 0x14, 0x00)), 1.5);
        var cutCapBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x14, 0x00));
        var labelBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        var typeface = new Typeface("Consolas");

        bg.Freeze();
        clipLaneBg.Freeze();
        lanePen.Freeze();
        tickPen.Freeze();
        majorTickPen.Freeze();
        playheadPen.Freeze();
        inOutPen.Freeze();
        selectedPen.Freeze();
        markedPen.Freeze();
        cutPen.Freeze();
        cutCapBrush.Freeze();
        labelBrush.Freeze();

        // 背景：整条底色 + 裁切片段轴稍亮，便于分辨三条并排轴
        dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));
        dc.DrawRectangle(clipLaneBg, null, new Rect(0, clipTop, w, ClipAxisHeight));

        if (lenMs <= 0)
        {
            DrawAxisSeparators(dc, lanePen, w, clipTop, clipBottom);
            return;
        }

        // --- 轴1：时间滑块轴（刻度 + 时间标签，底部对齐刻度线） ---
        // 根据 Scale 决定刻度间隔
        var pixelsPerSecond = w / len.TotalSeconds;
        double labelIntervalSeconds;
        if (pixelsPerSecond > 200) labelIntervalSeconds = 1;
        else if (pixelsPerSecond > 100) labelIntervalSeconds = 2;
        else if (pixelsPerSecond > 50) labelIntervalSeconds = 5;
        else if (pixelsPerSecond > 20) labelIntervalSeconds = 10;
        else if (pixelsPerSecond > 10) labelIntervalSeconds = 30;
        else if (pixelsPerSecond > 5) labelIntervalSeconds = 60;
        else labelIntervalSeconds = 120;

        var totalSeconds = len.TotalSeconds;
        for (double s = 0; s <= totalSeconds; s += labelIntervalSeconds)
        {
            var x = s / totalSeconds * w;
            // 主刻度线（紧贴轴1底边，避免压到时间标签）
            dc.DrawLine(majorTickPen, new Point(x, RulerAxisHeight - 7), new Point(x, RulerAxisHeight));
            // 时间标签
            var ts = TimeSpan.FromSeconds(s);
            var label = ts.TotalHours >= 1
                ? ts.ToString(@"h\:mm\:ss")
                : ts.ToString(@"mm\:ss");
            var ft = new FormattedText(label, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 10, labelBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(x + 2, 0));
        }
        // 小刻度（每秒一个，仅缩放足够时）
        if (pixelsPerSecond > 20)
        {
            for (double s = 0; s <= totalSeconds; s += 1)
            {
                if (s % labelIntervalSeconds == 0) continue;
                var x = s / totalSeconds * w;
                dc.DrawLine(tickPen, new Point(x, RulerAxisHeight - 4), new Point(x, RulerAxisHeight));
            }
        }

        // --- 轴2：裁切片段轴 ---
        // 选中段高亮（播放头所在片段：白色 10% + 灰细边框）
        if (SelectedSegmentStart is { } selStart && SelectedSegmentEnd is { } selEnd && selEnd > selStart)
        {
            var sx = selStart.TotalMilliseconds / lenMs * w;
            var ex = selEnd.TotalMilliseconds / lenMs * w;
            dc.DrawRectangle(selectedBrush, selectedPen,
                new Rect(sx, clipTop, Math.Max(0, ex - sx), ClipAxisHeight));
        }

        // 已标记片段（黄色半透明段带 + 黄边框，叠加于选中高亮之上）
        if (MarkedSegments is not null)
        {
            foreach (var seg in MarkedSegments)
            {
                if (seg.End <= seg.Start || seg.End <= TimeSpan.Zero) continue;
                var mx = seg.Start.TotalMilliseconds / lenMs * w;
                var mex = seg.End.TotalMilliseconds / lenMs * w;
                dc.DrawRectangle(markedBrush, markedPen,
                    new Rect(mx, clipTop, Math.Max(0, mex - mx), ClipAxisHeight));
            }
        }

        // I/O 入出点（只落在裁切片段轴）
        if (InPoint is { } inP)
        {
            var inX = inP.TotalMilliseconds / lenMs * w;
            double outX = OutPoint is { } outP
                ? outP.TotalMilliseconds / lenMs * w
                : w;
            // 容错：历史会话数据可能出现 In>Out 或 In 超出时长，宽度必须非负（否则渲染抛异常）
            var ioWidth = Math.Max(0, outX - inX);
            if (ioWidth > 0)
                dc.DrawRectangle(inOutBrush, null, new Rect(inX, clipTop, ioWidth, ClipAxisHeight));
            dc.DrawLine(inOutPen, new Point(inX, clipTop), new Point(inX, clipBottom));
            if (OutPoint is not null)
                dc.DrawLine(inOutPen, new Point(outX, clipTop), new Point(outX, clipBottom));
        }

        // 裁切点（红色竖线 + 轴2 顶部三角）
        if (CutPoints is not null)
        {
            foreach (var cp in CutPoints)
            {
                var x = cp.TotalMilliseconds / lenMs * w;
                dc.DrawLine(cutPen, new Point(x, clipTop), new Point(x, clipBottom));
                var tri = new StreamGeometry();
                using (var ctx = tri.Open())
                {
                    ctx.BeginFigure(new Point(x - 4, clipTop), true, true);
                    ctx.LineTo(new Point(x + 4, clipTop), true, false);
                    ctx.LineTo(new Point(x, clipTop + 8), true, false);
                }
                tri.Freeze();
                dc.DrawGeometry(cutCapBrush, null, tri);
            }
        }

        // --- 轴3：截图标记轴（截图色块：统一 8×8 圆角小方块，垂直居中） ---
        if (Markers is not null)
        {
            var markerY = markerTop + (markerHeight - 8) / 2;
            foreach (var marker in Markers)
            {
                var x = marker.Position.TotalMilliseconds / lenMs * w;
                dc.DrawRoundedRectangle(GetMarkerBrush(marker.ColorHex), null,
                    new Rect(x - 4, markerY, 8, 8), 2, 2);
            }
        }

        // --- 拖拽虚影 + 时间码气泡（v1.7.0 标记拖拽；播放头之下一层） ---
        if (_dragMoved && _dragMode is DragMode.CutPoint or DragMode.Screenshot)
        {
            var gx = _dragCurrent.TotalMilliseconds / lenMs * w;
            double bubbleTop;
            if (_dragMode == DragMode.CutPoint)
            {
                var ghostPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE5, 0x14, 0x00)), 2.5);
                dc.DrawLine(ghostPen, new Point(gx, clipTop), new Point(gx, clipBottom));
                dc.DrawLine(ghostPen, new Point(gx - 5, clipTop + 8), new Point(gx + 5, clipTop + 8));
                bubbleTop = clipTop + 2;
            }
            else
            {
                dc.DrawRoundedRectangle(GetMarkerBrush("#4EC9B0"), null,
                    new Rect(gx - 5, markerTop + 4, 10, 10), 2, 2);
                bubbleTop = markerTop + 2;
            }
            // 时间码气泡（半透明黑底白字，跟随虚影）
            var dragLabel = _dragCurrent.TotalHours >= 1
                ? _dragCurrent.ToString(@"h\:mm\:ss\.f")
                : _dragCurrent.ToString(@"mm\:ss\.f");
            var bubbleBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
            var bubbleBg = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00)) { Opacity = 0.75 };
            var dragFt = new FormattedText(dragLabel, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 10, bubbleBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var bx = Math.Clamp(gx - dragFt.Width / 2, 0, Math.Max(0, w - dragFt.Width));
            dc.DrawRectangle(bubbleBg, null, new Rect(bx - 3, bubbleTop, dragFt.Width + 6, dragFt.Height + 4));
            dc.DrawText(dragFt, new Point(bx, bubbleTop + 2));
        }

        // 轴分隔线（压在内容之上，保证三条轴界限始终可辨）
        DrawAxisSeparators(dc, lanePen, w, clipTop, clipBottom);

        // --- 播放头（贯穿三轴的白色竖线 + 轴1 顶部三角，PR 风格） ---
        var phX = PositionRatio * w;
        dc.DrawLine(playheadPen, new Point(phX, 0), new Point(phX, h));
        var phTri = new StreamGeometry();
        using (var ctx = phTri.Open())
        {
            ctx.BeginFigure(new Point(phX - 6, 0), true, true);
            ctx.LineTo(new Point(phX + 6, 0), true, false);
            ctx.LineTo(new Point(phX, 10), true, false);
        }
        phTri.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)), null, phTri);
    }

    private static void DrawAxisSeparators(
        DrawingContext dc, Pen pen, double width, double clipTop, double clipBottom)
    {
        dc.DrawLine(pen, new Point(0, clipTop), new Point(width, clipTop));
        dc.DrawLine(pen, new Point(0, clipBottom), new Point(width, clipBottom));
    }

    // --- 鼠标交互 ---

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        _dragStartX = e.GetPosition(this).X;
        _dragMoved = false;

        // 命中检测优先：命中裁切点/截图标记 → 拖拽模式（不 seek）
        if (TryHitTestMarker(e, out var mode, out var hitPos))
        {
            _dragMode = mode;
            _dragOriginal = hitPos;
            _dragCurrent = hitPos;
            CaptureMouse();
            Cursor = Cursors.SizeWE;
            e.Handled = true;
            return;
        }

        _dragMode = DragMode.Seek;
        CaptureMouse();
        SeekToMouse(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragMode is DragMode.CutPoint or DragMode.Screenshot)
        {
            _dragCurrent = MouseToTime(e);
            if (Math.Abs(e.GetPosition(this).X - _dragStartX) > 3)
                _dragMoved = true;
            // 实时 seek 预览：播放头与画面跟随拖拽位置
            SeekToMouse(e);
            InvalidateVisual();
        }
        else if (_dragMode == DragMode.Seek)
        {
            SeekToMouse(e);
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        var mode = _dragMode;
        _dragMode = DragMode.None;
        ReleaseMouseCapture();
        Cursor = null;

        if (mode is DragMode.CutPoint or DragMode.Screenshot)
        {
            if (_dragMoved)
            {
                // 拖拽提交：只触发一次数据落盘事件（不触发段选中）
                var final = MouseToTime(e);
                if (mode == DragMode.CutPoint)
                    CutPointDragged?.Invoke(this, new MarkerDragEventArgs(_dragOriginal, final));
                else
                    ScreenshotDragged?.Invoke(this, new MarkerDragEventArgs(_dragOriginal, final));
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            // 未构成拖拽：按普通点击处理（seek + 段选中）
            SeekToMouse(e);
        }

        if (CutPoints is not null && Length > TimeSpan.Zero)
        {
            var time = MouseToTime(e);
            SegmentSelected?.Invoke(this, time);
        }
    }

    /// <summary>
    /// 命中检测（v1.8.0）：按三轴分区——轴2（裁切片段轴）只命中裁切点，
    /// 轴3（截图标记轴）只命中截图标记；容差约 6px 换算为时间。
    /// </summary>
    private bool TryHitTestMarker(MouseEventArgs e, out DragMode mode, out TimeSpan hitPos)
    {
        mode = DragMode.None;
        hitPos = default;
        if (ActualWidth <= 0 || Length <= TimeSpan.Zero) return false;

        var toleranceMs = HitTolerancePx / ActualWidth * Length.TotalMilliseconds;
        var mouseTime = MouseToTime(e);
        var y = e.GetPosition(this).Y;

        if (CutPoints is not null && y >= ClipAxisTop && y < MarkerAxisTop)
        {
            var list = CutPoints as IReadOnlyList<TimeSpan> ?? CutPoints.ToList();
            var idx = TimelineHitTest.FindNearest(list, mouseTime, toleranceMs);
            if (idx >= 0)
            {
                mode = DragMode.CutPoint;
                hitPos = list[idx];
                return true;
            }
        }

        if (Markers is not null && y >= MarkerAxisTop)
        {
            TimelineMarker? bestMarker = null;
            var bestMs = double.MaxValue;
            foreach (var m in Markers)
            {
                if (m.Kind != MarkerKind.Screenshot) continue;
                var ms = Math.Abs((m.Position - mouseTime).TotalMilliseconds);
                if (ms < bestMs) { bestMs = ms; bestMarker = m; }
            }
            if (bestMarker is not null && bestMs <= toleranceMs)
            {
                mode = DragMode.Screenshot;
                hitPos = bestMarker.Position;
                return true;
            }
        }
        return false;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // 鼠标滚轮缩放
        var factor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
        Scale *= factor;
        e.Handled = true;
    }

    private TimeSpan MouseToTime(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        var ratio = ActualWidth > 0 ? pos.X / ActualWidth : 0;
        ratio = Math.Clamp(ratio, 0, 1);
        return TimeSpan.FromMilliseconds(ratio * Length.TotalMilliseconds);
    }

    private void SeekToMouse(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        var ratio = ActualWidth > 0 ? pos.X / ActualWidth : 0;
        ratio = Math.Clamp(ratio, 0, 1);
        SeekRequested?.Invoke(this, ratio);
    }
}
