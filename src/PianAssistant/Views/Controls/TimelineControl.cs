using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PianAssistant.Models;

namespace PianAssistant.Views.Controls;

/// <summary>
/// 时间轴控件：可缩放、刻度+时间标签、PR 风格播放头（白色竖线+顶部三角）、
/// I/O 入出点高亮、PR 风格裁切线（红色）、选中段。
/// 鼠标滚轮缩放，点击/拖动 seek，释放时触发段选中。
/// </summary>
public sealed class TimelineControl : Control
{
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
        control.InvalidateVisual();
    }

    private void OnMarkedSegmentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => InvalidateVisual();

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

    private bool _isDragging;
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
        return new Size(desiredWidth, 40);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var w = ActualWidth; // 缩放后的完整宽度
        var h = ActualHeight;
        var len = Length;
        var lenMs = len.TotalMilliseconds;

        // 画笔（色彩规范：裁切点红 #E51400 / 截图点绿 #4EC9B0 / 标记片段黄 #D7BA7D /
        // 选中段白 10% / I/O 蓝 #009AEC / 播放头白 —— 与笔记列表 AccentColor 对齐）
        var bg = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
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
        tickPen.Freeze();
        majorTickPen.Freeze();
        playheadPen.Freeze();
        inOutPen.Freeze();
        selectedPen.Freeze();
        markedPen.Freeze();
        cutPen.Freeze();
        cutCapBrush.Freeze();
        labelBrush.Freeze();

        // Background
        dc.DrawRectangle(bg, null, new Rect(0, 0, w, h));

        if (lenMs <= 0) return;

        // --- 刻度 + 时间标签 ---
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
            // 主刻度线
            dc.DrawLine(majorTickPen, new Point(x, h * 0.5), new Point(x, h));
            // 时间标签
            var ts = TimeSpan.FromSeconds(s);
            var label = ts.TotalHours >= 1
                ? ts.ToString(@"h\:mm\:ss")
                : ts.ToString(@"mm\:ss");
            var ft = new FormattedText(label, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 10, labelBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(ft, new Point(x + 2, 2));
        }
        // 小刻度（每秒一个，仅缩放足够时）
        if (pixelsPerSecond > 20)
        {
            for (double s = 0; s <= totalSeconds; s += 1)
            {
                if (s % labelIntervalSeconds == 0) continue;
                var x = s / totalSeconds * w;
                dc.DrawLine(tickPen, new Point(x, h * 0.75), new Point(x, h));
            }
        }

        // --- 选中段高亮（播放头所在片段：白色 10% + 灰细边框） ---
        if (SelectedSegmentStart is { } selStart && SelectedSegmentEnd is { } selEnd && selEnd > selStart)
        {
            var sx = selStart.TotalMilliseconds / lenMs * w;
            var ex = selEnd.TotalMilliseconds / lenMs * w;
            dc.DrawRectangle(selectedBrush, selectedPen, new Rect(sx, 0, Math.Max(0, ex - sx), h));
        }

        // --- 已标记片段（黄色半透明段带 + 黄边框，叠加于选中高亮之上） ---
        if (MarkedSegments is not null)
        {
            foreach (var seg in MarkedSegments)
            {
                if (seg.End <= seg.Start || seg.End <= TimeSpan.Zero) continue;
                var mx = seg.Start.TotalMilliseconds / lenMs * w;
                var mex = seg.End.TotalMilliseconds / lenMs * w;
                dc.DrawRectangle(markedBrush, markedPen,
                    new Rect(mx, 0, Math.Max(0, mex - mx), h));
            }
        }

        // --- I/O 入出点 ---
        if (InPoint is { } inP)
        {
            var inX = inP.TotalMilliseconds / lenMs * w;
            double outX = OutPoint is { } outP
                ? outP.TotalMilliseconds / lenMs * w
                : w;
            // 容错：历史会话数据可能出现 In>Out 或 In 超出时长，宽度必须非负（否则渲染抛异常）
            var ioWidth = Math.Max(0, outX - inX);
            if (ioWidth > 0)
                dc.DrawRectangle(inOutBrush, null, new Rect(inX, 0, ioWidth, h));
            dc.DrawLine(inOutPen, new Point(inX, 0), new Point(inX, h));
            if (OutPoint is not null)
                dc.DrawLine(inOutPen, new Point(outX, 0), new Point(outX, h));
        }

        // --- 裁切点（红色竖线 + 顶部三角） ---
        if (CutPoints is not null)
        {
            foreach (var cp in CutPoints)
            {
                var x = cp.TotalMilliseconds / lenMs * w;
                dc.DrawLine(cutPen, new Point(x, 8), new Point(x, h));
                // 顶部三角
                var tri = new StreamGeometry();
                using (var ctx = tri.Open())
                {
                    ctx.BeginFigure(new Point(x - 4, 0), true, true);
                    ctx.LineTo(new Point(x + 4, 0), true, false);
                    ctx.LineTo(new Point(x, 8), true, false);
                }
                tri.Freeze();
                dc.DrawGeometry(cutCapBrush, null, tri);
            }
        }

        // --- 标记（截图点：绿色统一 6×6 圆角小方块）---
        // 位于播放头三角下方一行（y 9~15），不与裁切点红三角（y 0~8）冲突
        if (Markers is not null)
        {
            foreach (var marker in Markers)
            {
                var x = marker.Position.TotalMilliseconds / lenMs * w;
                dc.DrawRoundedRectangle(GetMarkerBrush(marker.ColorHex), null,
                    new Rect(x - 3, 9, 6, 6), 1.5, 1.5);
            }
        }

        // --- 播放头（白色竖线 + 顶部三角，PR 风格） ---
        var phX = PositionRatio * w;
        dc.DrawLine(playheadPen, new Point(phX, 0), new Point(phX, h));
        // 顶部三角播放头标记
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

    // --- 鼠标交互 ---

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        _isDragging = true;
        CaptureMouse();
        SeekToMouse(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_isDragging)
            SeekToMouse(e);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        _isDragging = false;
        ReleaseMouseCapture();
        if (CutPoints is not null && Length > TimeSpan.Zero)
        {
            var time = MouseToTime(e);
            SegmentSelected?.Invoke(this, time);
        }
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
