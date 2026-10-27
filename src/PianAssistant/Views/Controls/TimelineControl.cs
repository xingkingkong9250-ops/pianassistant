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
/// 时间轴控件：可缩放、播放头（白色竖线+上下三角沙漏形）、I/O 入出点、选中段。
/// 鼠标滚轮缩放，点击/拖动 seek，释放时触发段选中。
/// v1.7.0：裁切点/截图标记支持选中拖拽修改（虚影 + 实时 seek 预览，松手提交）。
/// v1.8.0：拆为三条并排轴——时间滑块轴（刻度/播放头/seek）、裁切片段轴（裁切点/
/// 入出点/选中段/已标记片段）、截图标记轴（水滴标记）；各轴命中互不干扰。
/// v1.14.0：整体缩窄（56→46），播放头改上下三角沙漏形，截图标记改水滴形。
/// </summary>
public sealed class TimelineControl : Control
{
    /// <summary>轴1 时间滑块轴高度：刻度、时间标签、播放头顶三角。</summary>
    public const double RulerAxisHeight = 18;

    /// <summary>轴2 裁切片段轴高度：裁切点、入出点、选中段、已标记片段。</summary>
    public const double ClipAxisHeight = 14;

    /// <summary>轴3 截图标记轴高度：水滴标记。</summary>
    public const double MarkerAxisHeight = 14;

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
        GoldFillBrush.Freeze();
        SelectedStrokePen.Freeze();
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
                FrameworkPropertyMetadataOptions.AffectsRender, OnMarkersChanged));

    private static void OnMarkersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TimelineControl control) return;
        // 反订阅旧集合 / 旧 marker 属性变化（v1.15.1：TimelineMarker 变可观察）
        if (e.OldValue is IEnumerable<TimelineMarker> oldMarkers)
        {
            foreach (var m in oldMarkers)
                m.PropertyChanged -= control.OnMarkerPropertyChanged;
            if (e.OldValue is INotifyCollectionChanged oldCol)
                oldCol.CollectionChanged -= control.OnMarkersCollectionChanged;
        }
        // 订阅新集合 / 新 marker
        if (e.NewValue is INotifyCollectionChanged newCol)
            newCol.CollectionChanged += control.OnMarkersCollectionChanged;
        if (e.NewValue is IEnumerable<TimelineMarker> newMarkers)
        {
            foreach (var m in newMarkers)
                m.PropertyChanged += control.OnMarkerPropertyChanged;
        }
        control.InvalidateTimelineVisual();
    }

    private void OnMarkersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 集合内容增删时：新 marker 订阅，旧 marker 反订阅
        if (e.OldItems is not null)
            foreach (TimelineMarker m in e.OldItems)
                m.PropertyChanged -= OnMarkerPropertyChanged;
        if (e.NewItems is not null)
            foreach (TimelineMarker m in e.NewItems)
                m.PropertyChanged += OnMarkerPropertyChanged;
        InvalidateTimelineVisual();
    }

    /// <summary>TimelineMarker 内部 IsSelected / IsMarked 变化 → 单 marker 级触发重绘（v1.15.1）。</summary>
    private void OnMarkerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(TimelineMarker.IsSelected) or nameof(TimelineMarker.IsMarked))) return;
        InvalidateTimelineVisual();
    }

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
    // v1.15.1 固定态画笔（避免 OnRender 每帧创建：已 Freeze，线程安全）
    private static readonly SolidColorBrush GoldFillBrush =
        new(Color.FromRgb(0xD7, 0xBA, 0x7D));
    private static readonly Pen SelectedStrokePen =
        new(Brushes.White, 0.8);

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
        HookParentScrollViewer();
    }

    // v1.13.0：刻度标签按视口可见区段绘制，水平滚动时必须重绘
    private ScrollViewer? _hookedScrollViewer;

    private void HookParentScrollViewer()
    {
        var sv = FindAncestorScrollViewer();
        if (sv is null || ReferenceEquals(sv, _hookedScrollViewer)) return;
        if (_hookedScrollViewer is not null)
            _hookedScrollViewer.ScrollChanged -= OnParentScrollChanged;
        sv.ScrollChanged += OnParentScrollChanged;
        _hookedScrollViewer = sv;
    }

    private void OnParentScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.HorizontalChange != 0)
            InvalidateTimelineVisual();
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
        // v1.15.0：主刻度参考线（延伸到轴2/轴3，比轴1刻度更淡，便于跨轴定位不喧宾夺主）
        var guidePen = new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), 1);
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
        guidePen.Freeze();
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
        // v1.13.0：只绘制视口可见区段，间隔按像素密度自适应取「整齐值」，
        // 标签带偏移钳制（左缘不溢出、右缘不超出），不再挤成一团。
        var pixelsPerSecond = w / len.TotalSeconds;
        double visibleStartSec = 0, visibleEndSec = len.TotalSeconds;
        if (FindAncestorScrollViewer() is { } host && _viewportWidth > 0)
        {
            visibleStartSec = Math.Max(0, host.HorizontalOffset / w) * len.TotalSeconds;
            visibleEndSec = Math.Min(1, (host.HorizontalOffset + _viewportWidth) / w) * len.TotalSeconds;
        }

        var labelStep = PickRulerInterval(90.0 / pixelsPerSecond);   // 主刻度+标签 ≥90px
        var minorStep = PickRulerInterval(9.0 / pixelsPerSecond);    // 小刻度 ≥9px

        var minorFirst = Math.Floor(visibleStartSec / minorStep) * minorStep;
        for (double s = minorFirst; s <= visibleEndSec + 1e-9; s += minorStep)
        {
            if (s < 0 || s > len.TotalSeconds) continue;
            if (Math.Abs(s / labelStep - Math.Round(s / labelStep)) < 1e-6) continue; // 主刻度单独画
            var x = s / len.TotalSeconds * w;
            dc.DrawLine(tickPen, new Point(x, RulerAxisHeight - 3.5), new Point(x, RulerAxisHeight));
        }

        var labelFirst = Math.Floor(visibleStartSec / labelStep) * labelStep;
        for (double s = labelFirst; s <= visibleEndSec + 1e-9; s += labelStep)
        {
            if (s < 0 || s > len.TotalSeconds) continue;
            var x = s / len.TotalSeconds * w;
            dc.DrawLine(majorTickPen, new Point(x, RulerAxisHeight - 6), new Point(x, RulerAxisHeight));
            // v1.15.0：主刻度参考线贯穿轴2/轴3（裁切/截图行），便于定位
            dc.DrawLine(guidePen, new Point(x, ClipAxisTop), new Point(x, h));
            var ts = TimeSpan.FromSeconds(s);
            var label = FormatRulerLabel(ts, labelStep);
            var ft = new FormattedText(label, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 10, labelBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            // 钳制：标签不越出时间轴左右边界（避免 0 点溢出左侧、末尾被裁切）
            var tx = Math.Clamp(x + 2, 0, Math.Max(0, w - ft.Width - 1));
            dc.DrawText(ft, new Point(tx, 0));
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

        // 裁切点（v1.14.0 旗标：红色竖线旗杆 + 轴2 顶部小三角旗面）
        if (CutPoints is not null)
        {
            foreach (var cp in CutPoints)
            {
                var x = cp.TotalMilliseconds / lenMs * w;
                dc.DrawLine(cutPen, new Point(x, clipTop), new Point(x, clipBottom));
                var tri = new StreamGeometry();
                using (var ctx = tri.Open())
                {
                    ctx.BeginFigure(new Point(x - 3.5, clipTop), true, true);
                    ctx.LineTo(new Point(x + 3.5, clipTop), true, false);
                    ctx.LineTo(new Point(x, clipTop + 6), true, false);
                }
                tri.Freeze();
                dc.DrawGeometry(cutCapBrush, null, tri);
            }
        }

        // --- 轴3：截图标记轴（v1.14.0 水滴标记：圆头 + 下尖角，垂直居中；
        //     v1.15.1：IsMarked → 金色（与轴2黄带同色系），IsSelected → 白色描边高亮） ---
        if (Markers is not null)
        {
            var cy = markerTop + markerHeight / 2;
            foreach (var marker in Markers)
            {
                var x = marker.Position.TotalMilliseconds / lenMs * w;
                // IsMarked 优先（核心导出提示金）；IsSelected 白色描边叠加
                var fill = marker.IsMarked ? GoldFillBrush : GetMarkerBrush(marker.ColorHex);
                var stroke = marker.IsSelected ? SelectedStrokePen : null;
                dc.DrawGeometry(fill, stroke, BuildDropGeometry(x, cy));
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
                dc.DrawGeometry(GetMarkerBrush("#4EC9B0"), null,
                    BuildDropGeometry(gx, markerTop + MarkerAxisHeight / 2 + 1));
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

        // --- 播放头（v1.14.0 沙漏形：白色竖线贯穿三轴 + 顶部下三角 + 底部上三角） ---
        var phX = PositionRatio * w;
        dc.DrawLine(playheadPen, new Point(phX, 0), new Point(phX, h));
        var phBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
        var phTop = new StreamGeometry();
        using (var ctx = phTop.Open())
        {
            ctx.BeginFigure(new Point(phX - 5, 0), true, true);
            ctx.LineTo(new Point(phX + 5, 0), true, false);
            ctx.LineTo(new Point(phX, 7), true, false);
        }
        phTop.Freeze();
        dc.DrawGeometry(phBrush, null, phTop);
        var phBottom = new StreamGeometry();
        using (var ctx = phBottom.Open())
        {
            ctx.BeginFigure(new Point(phX - 5, h), true, true);
            ctx.LineTo(new Point(phX + 5, h), true, false);
            ctx.LineTo(new Point(phX, h - 7), true, false);
        }
        phBottom.Freeze();
        dc.DrawGeometry(phBrush, null, phBottom);
    }

    /// <summary>构建水滴形几何（v1.14.0 截图标记）：圆头在上、尖角朝下。</summary>
    private static Geometry BuildDropGeometry(double x, double cy)
    {
        var head = new EllipseGeometry(new Point(x, cy - 1.4), 2.6, 2.6);
        var tip = new StreamGeometry();
        using (var ctx = tip.Open())
        {
            ctx.BeginFigure(new Point(x - 2.2, cy - 1.6), true, true);
            ctx.LineTo(new Point(x + 2.2, cy - 1.6), true, false);
            ctx.LineTo(new Point(x, cy + 3.6), true, false);
        }
        tip.Freeze();
        var drop = new CombinedGeometry(GeometryCombineMode.Union, head, tip);
        drop.Freeze();
        return drop;
    }

    // 整齐刻度阶梯（秒）：1/2/5/10/15/30 → 1/2/5/10/15/30 分 → 1/2/3/6/12 小时
    private static readonly double[] RulerStepsSeconds =
    {
        1, 2, 5, 10, 15, 30,
        60, 120, 300, 600, 900, 1800,
        3600, 7200, 10800, 21600, 43200,
    };

    /// <summary>从整齐阶梯中选「不小于 minSeconds」的最小间隔（v1.13.0 自适应刻度）。</summary>
    internal static double PickRulerInterval(double minSeconds)
    {
        foreach (var step in RulerStepsSeconds)
        {
            if (step >= minSeconds) return step;
        }
        return RulerStepsSeconds[^1];
    }

    /// <summary>刻度标签格式化：短轴用 mm:ss，跨小时或间隔≥1小时时用 h:mm:ss（v1.13.0）。</summary>
    internal static string FormatRulerLabel(TimeSpan ts, double stepSeconds)
    {
        if (stepSeconds >= 3600 || ts.TotalHours >= 1)
            return ts.ToString(@"h\:mm\:ss");
        return ts.ToString(@"mm\:ss");
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

    /// <summary>向上查找承载本控件的 ScrollViewer（直接父级可能是包装 Grid）。</summary>
    private ScrollViewer? FindAncestorScrollViewer()
    {
        DependencyObject? current = Parent;
        while (current is not null)
        {
            if (current is ScrollViewer sv) return sv;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // 鼠标滚轮缩放（v1.13.0：以鼠标所指时间点为锚，两端拉长/缩短）
        var factor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
        var oldScale = Scale;
        var newScale = Math.Clamp(oldScale * factor, 0.5, 50.0);
        if (Math.Abs(newScale - oldScale) < 1e-9)
        {
            e.Handled = true;
            return;
        }

        var contentX = e.GetPosition(this).X; // 鼠标所指内容坐标（= 时间点）
        double? pendingOffset = null;
        if (FindAncestorScrollViewer() is { } sv)
        {
            var viewX = contentX - sv.HorizontalOffset; // 视口内相对位置
            pendingOffset = contentX * (newScale / oldScale) - viewX;
        }

        Scale = newScale; // 会同步更新 Width

        // 等布局更新 Extent 后再校正滚动位置，保持鼠标下的时间点不动
        if (pendingOffset is { } target)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (FindAncestorScrollViewer() is { } host)
                    host.ScrollToHorizontalOffset(Math.Max(0, target));
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
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
