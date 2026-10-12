using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PianAssistant.Views.Controls;

/// <summary>
/// 时间轴控件：刻度、播放头、I/O 入出点高亮、PR 风格裁切线、选中段。
/// 点击/拖动触发 <see cref="SeekRequested"/>；点击裁切点之间触发 <see cref="SegmentSelected"/>。
/// </summary>
public sealed class TimelineControl : Control
{
    static TimelineControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(TimelineControl),
            new FrameworkPropertyMetadata(typeof(TimelineControl)));
    }

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

    /// <summary>点击/拖动 seek，值为 0..1。</summary>
    public event EventHandler<double>? SeekRequested;

    /// <summary>点击裁切段时触发，值为点击位置的时间。</summary>
    public event EventHandler<TimeSpan>? SegmentSelected;

    private bool _isDragging;

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        var bg = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        var tickPen = new Pen(new SolidColorBrush(Color.FromRgb(0x45, 0x45, 0x48)), 1);
        var playheadPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x00)), 2);
        var inOutBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)) { Opacity = 0.3 };
        var inOutPen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x9A, 0xEC)), 1.5);
        var selectedBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x4B, 0x4B)) { Opacity = 0.2 };
        var cutPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0x4B, 0x4B)), 2);
        var cutCapBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x4B, 0x4B));

        bg.Freeze();
        tickPen.Freeze();
        playheadPen.Freeze();
        inOutPen.Freeze();
        cutPen.Freeze();
        cutCapBrush.Freeze();

        // Background
        dc.DrawRectangle(bg, null, rect);

        // Selected segment highlight (PR 风格)
        if (SelectedSegmentStart is { } selStart && SelectedSegmentEnd is { } selEnd
            && Length > TimeSpan.Zero && selEnd > selStart)
        {
            var sx = selStart.TotalMilliseconds / Length.TotalMilliseconds * ActualWidth;
            var ex = selEnd.TotalMilliseconds / Length.TotalMilliseconds * ActualWidth;
            dc.DrawRectangle(selectedBrush, null, new Rect(sx, 0, ex - sx, ActualHeight));
        }

        // In/out highlight band
        if (InPoint is { } inP && Length > TimeSpan.Zero)
        {
            var inX = inP.TotalMilliseconds / Length.TotalMilliseconds * ActualWidth;
            double outX = OutPoint is { } outP
                ? outP.TotalMilliseconds / Length.TotalMilliseconds * ActualWidth
                : ActualWidth;

            dc.DrawRectangle(inOutBrush, null,
                new Rect(inX, 0, outX - inX, ActualHeight));
            dc.DrawLine(inOutPen, new Point(inX, 0), new Point(inX, ActualHeight));
            if (OutPoint is not null)
                dc.DrawLine(inOutPen, new Point(outX, 0), new Point(outX, ActualHeight));
        }

        // Ticks: 10 evenly spaced
        for (int i = 0; i <= 10; i++)
        {
            var x = ActualWidth * i / 10;
            var y0 = ActualHeight * 0.7;
            dc.DrawLine(tickPen, new Point(x, y0), new Point(x, ActualHeight));
        }

        // Cut points (PR 剃刀风格红色竖线 + 顶部三角)
        if (CutPoints is not null && Length > TimeSpan.Zero)
        {
            foreach (var cp in CutPoints)
            {
                var x = cp.TotalMilliseconds / Length.TotalMilliseconds * ActualWidth;
                dc.DrawLine(cutPen, new Point(x, 0), new Point(x, ActualHeight));
                // 顶部小三角作为切口标记
                var tri = new StreamGeometry();
                using (var ctx = tri.Open())
                {
                    ctx.BeginFigure(new Point(x - 4, 0), true, true);
                    ctx.LineTo(new Point(x + 4, 0), true, false);
                    ctx.LineTo(new Point(x, 6), true, false);
                }
                tri.Freeze();
                dc.DrawGeometry(cutCapBrush, null, tri);
            }
        }

        // Playhead
        var phX = PositionRatio * ActualWidth;
        dc.DrawLine(playheadPen, new Point(phX, 0), new Point(phX, ActualHeight));
        dc.DrawEllipse(Brushes.Yellow, null, new Point(phX, ActualHeight / 2), 4, 4);
    }

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
        // 鼠标释放时，如果有裁切点，触发段选中
        if (CutPoints is not null && Length > TimeSpan.Zero)
        {
            var pos = e.GetPosition(this);
            var ratio = ActualWidth > 0 ? pos.X / ActualWidth : 0;
            ratio = Math.Clamp(ratio, 0, 1);
            var time = TimeSpan.FromMilliseconds(ratio * Length.TotalMilliseconds);
            SegmentSelected?.Invoke(this, time);
        }
    }

    private void SeekToMouse(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        var ratio = ActualWidth > 0 ? pos.X / ActualWidth : 0;
        ratio = Math.Clamp(ratio, 0, 1);
        SeekRequested?.Invoke(this, ratio);
    }
}
