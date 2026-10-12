using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PianAssistant.Views.Controls;

/// <summary>
/// A custom timeline strip showing tick marks, a playhead, and optional
/// in/out point markers. The user can click or drag to seek.
/// Raises <see cref="SeekRequested"/> with a 0..1 ratio.
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

    /// <summary>Raised when the user clicks/drags to seek. Value is 0..1.</summary>
    public event EventHandler<double>? SeekRequested;

    private bool _isDragging;

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        var bg = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        var tickPen = new Pen(new SolidColorBrush(Color.FromRgb(0x45, 0x45, 0x48)), 1);
        var playheadPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x00)), 2);
        var inOutBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)) { Opacity = 0.3 };
        var inOutPen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x9A, 0xEC)), 1.5);
        var textBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));

        bg.Freeze();
        tickPen.Freeze();
        playheadPen.Freeze();

        // Background
        dc.DrawRectangle(bg, null, rect);

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
    }

    private void SeekToMouse(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        var ratio = ActualWidth > 0 ? pos.X / ActualWidth : 0;
        ratio = Math.Clamp(ratio, 0, 1);
        SeekRequested?.Invoke(this, ratio);
    }
}
