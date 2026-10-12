using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

    private static void OnScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TimelineControl tc)
        {
            // 更新控件宽度以适配缩放
            var baseWidth = tc._viewportWidth > 0 ? tc._viewportWidth : tc.ActualWidth;
            if (baseWidth > 0)
                tc.Width = baseWidth * tc.Scale;
            tc.ScaleChanged?.Invoke(tc, tc.Scale);
        }
    }

    /// <summary>由外部 ScrollViewer 设置可见区域宽度。</summary>
    public void SetViewportWidth(double width)
    {
        _viewportWidth = width;
        if (width > 0)
            Width = width * Scale;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsInfinity(availableSize.Width) ? _viewportWidth : availableSize.Width;
        if (w > 0 && _viewportWidth <= 0)
            _viewportWidth = w;
        var desiredWidth = (_viewportWidth > 0 ? _viewportWidth : w) * Scale;
        return new Size(desiredWidth, 40);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var w = ActualWidth; // 缩放后的完整宽度
        var h = ActualHeight;
        var len = Length;
        var lenMs = len.TotalMilliseconds;

        // 画笔
        var bg = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
        var tickPen = new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)), 1);
        var majorTickPen = new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1);
        var playheadPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)), 2);
        var inOutBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)) { Opacity = 0.25 };
        var inOutPen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x9A, 0xEC)), 1.5);
        var selectedBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x4B, 0x4B)) { Opacity = 0.15 };
        var cutPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0x4B, 0x4B)), 1.5);
        var cutCapBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x4B, 0x4B));
        var labelBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        var typeface = new Typeface("Consolas");

        bg.Freeze();
        tickPen.Freeze();
        majorTickPen.Freeze();
        playheadPen.Freeze();
        inOutPen.Freeze();
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

        // --- 选中段高亮 ---
        if (SelectedSegmentStart is { } selStart && SelectedSegmentEnd is { } selEnd && selEnd > selStart)
        {
            var sx = selStart.TotalMilliseconds / lenMs * w;
            var ex = selEnd.TotalMilliseconds / lenMs * w;
            dc.DrawRectangle(selectedBrush, null, new Rect(sx, 0, ex - sx, h));
        }

        // --- I/O 入出点 ---
        if (InPoint is { } inP)
        {
            var inX = inP.TotalMilliseconds / lenMs * w;
            double outX = OutPoint is { } outP
                ? outP.TotalMilliseconds / lenMs * w
                : w;
            dc.DrawRectangle(inOutBrush, null, new Rect(inX, 0, outX - inX, h));
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
