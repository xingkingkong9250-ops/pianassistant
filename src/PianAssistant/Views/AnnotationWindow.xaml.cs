using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PianAssistant.Models;

namespace PianAssistant.Views;

/// <summary>
/// 截图标注编辑器：左侧工具栏提供 选择/画笔(InkCanvas)/箭头/文字/裁剪 五种模式、
/// 四色（红/黄/蓝/白）与三档粗细。画布坐标系 = 图像像素（Viewbox 只负责显示缩放）。
/// 「保存为副本」用 RenderTargetBitmap 渲染标注层合成新图（原名_annotated.png），
/// 不修改原图；若执行了裁剪，以裁剪区为界输出。
/// </summary>
public partial class AnnotationWindow : Window
{
    private enum AnnotateMode { Select, Pen, Arrow, Text, Crop }

    private readonly ScreenshotEntry _entry;
    private readonly BitmapImage _bitmap;
    private AnnotateMode _mode = AnnotateMode.Pen;
    private Color _color = Colors.Red;
    private double _thickness = 3;

    // 裁剪状态（图像像素坐标）
    private Rect? _cropRect;
    private readonly List<UIElement> _cropElements = new();

    // 拖拽状态（箭头 / 裁剪共用）
    private bool _isDragging;
    private Point _dragStart;
    private Line? _previewLine;
    private Polygon? _previewHead;
    private Rectangle? _cropPreview;

    /// <summary>保存成功后的新图路径；取消为 null。</summary>
    public string? SavedPath { get; private set; }

    public AnnotationWindow(ScreenshotEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entry = entry;
        InitializeComponent();

        // 加载原图（OnLoad 确保此后文件可独立释放）
        _bitmap = new BitmapImage();
        _bitmap.BeginInit();
        _bitmap.CacheOption = BitmapCacheOption.OnLoad;
        _bitmap.UriSource = new Uri(entry.OutputPath, UriKind.Absolute);
        _bitmap.EndInit();
        _bitmap.Freeze();
        BaseImage.Source = _bitmap;

        // 画布坐标系 = 图像像素
        CanvasGrid.Width = _bitmap.PixelWidth;
        CanvasGrid.Height = _bitmap.PixelHeight;

        // 画笔默认属性：红色 / 3px / 圆头 / 平滑曲线
        AnnotationInk.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = _color,
            Width = _thickness,
            Height = _thickness,
            StylusTip = StylusTip.Ellipse,
            FitToCurve = true,
        };

        SetMode(AnnotateMode.Pen);
        SetColor(_color);
        SetThickness(_thickness);
    }

    // --- 模式切换 ---

    private void OnModeSelect(object sender, RoutedEventArgs e) => SetMode(AnnotateMode.Select);
    private void OnModePen(object sender, RoutedEventArgs e) => SetMode(AnnotateMode.Pen);
    private void OnModeArrow(object sender, RoutedEventArgs e) => SetMode(AnnotateMode.Arrow);
    private void OnModeText(object sender, RoutedEventArgs e) => SetMode(AnnotateMode.Text);
    private void OnModeCrop(object sender, RoutedEventArgs e) => SetMode(AnnotateMode.Crop);

    private void SetMode(AnnotateMode mode)
    {
        _mode = mode;
        HighlightTool(mode);

        switch (mode)
        {
            case AnnotateMode.Select:
                AnnotationInk.EditingMode = InkCanvasEditingMode.Select;
                ShapeCanvas.IsHitTestVisible = false;
                HintText.Text = "选择模式：拖动 / 缩放已有笔迹";
                break;
            case AnnotateMode.Pen:
                AnnotationInk.EditingMode = InkCanvasEditingMode.Ink;
                ShapeCanvas.IsHitTestVisible = false;
                HintText.Text = "画笔模式：按住左键绘制";
                break;
            case AnnotateMode.Arrow:
                AnnotationInk.EditingMode = InkCanvasEditingMode.None;
                ShapeCanvas.IsHitTestVisible = true;
                HintText.Text = "箭头模式：按下起点，拖到终点释放";
                break;
            case AnnotateMode.Text:
                AnnotationInk.EditingMode = InkCanvasEditingMode.None;
                ShapeCanvas.IsHitTestVisible = true;
                HintText.Text = "文字模式：点击位置输入文字，Enter 确认，Esc 取消";
                break;
            case AnnotateMode.Crop:
                AnnotationInk.EditingMode = InkCanvasEditingMode.None;
                ShapeCanvas.IsHitTestVisible = true;
                HintText.Text = "裁剪模式：拖出矩形，再点击「确认裁剪」";
                break;
        }
    }

    /// <summary>高亮当前模式按钮（强调色 #1A6B8A）。</summary>
    private void HighlightTool(AnnotateMode mode)
    {
        SetActive(BtnSelect, mode == AnnotateMode.Select);
        SetActive(BtnPen, mode == AnnotateMode.Pen);
        SetActive(BtnArrow, mode == AnnotateMode.Arrow);
        SetActive(BtnText, mode == AnnotateMode.Text);
        SetActive(BtnCrop, mode == AnnotateMode.Crop);
    }

    private static void SetActive(Button button, bool active)
    {
        button.Background = active
            ? new SolidColorBrush(Color.FromRgb(0x1A, 0x6B, 0x8A))
            : Brushes.Transparent;
        button.Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
    }

    // --- 颜色 / 粗细 ---

    private void OnColorRed(object sender, RoutedEventArgs e) => SetColor(Colors.Red);
    private void OnColorYellow(object sender, RoutedEventArgs e) => SetColor(Colors.Yellow);
    private void OnColorBlue(object sender, RoutedEventArgs e) => SetColor(Colors.DodgerBlue);
    private void OnColorWhite(object sender, RoutedEventArgs e) => SetColor(Colors.White);

    private void SetColor(Color color)
    {
        _color = color;
        AnnotationInk.DefaultDrawingAttributes.Color = color;
        HighlightColor(color);
    }

    private void HighlightColor(Color color)
    {
        SetActive(BtnRed, color == Colors.Red);
        SetActive(BtnYellow, color == Colors.Yellow);
        SetActive(BtnBlue, color == Colors.DodgerBlue);
        SetActive(BtnWhite, color == Colors.White);
    }

    private void OnThicknessThin(object sender, RoutedEventArgs e) => SetThickness(2);
    private void OnThicknessMedium(object sender, RoutedEventArgs e) => SetThickness(4);
    private void OnThicknessThick(object sender, RoutedEventArgs e) => SetThickness(8);

    private void SetThickness(double thickness)
    {
        _thickness = thickness;
        AnnotationInk.DefaultDrawingAttributes.Width = thickness;
        AnnotationInk.DefaultDrawingAttributes.Height = thickness;
        SetActive(BtnThin, thickness == 2);
        SetActive(BtnMedium, thickness == 4);
        SetActive(BtnThick, thickness == 8);
    }

    // --- 形状层交互（箭头 / 文字 / 裁剪）---

    private void OnShapeCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(ShapeCanvas);
        if (_mode == AnnotateMode.Arrow)
        {
            _isDragging = true;
            _dragStart = pos;
            _previewLine = new Line
            {
                Stroke = new SolidColorBrush(_color),
                StrokeThickness = _thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            _previewHead = new Polygon { Fill = new SolidColorBrush(_color) };
            ShapeCanvas.Children.Add(_previewLine);
            ShapeCanvas.Children.Add(_previewHead);
            UpdateArrow(pos);
            ShapeCanvas.CaptureMouse();
        }
        else if (_mode == AnnotateMode.Crop)
        {
            _isDragging = true;
            _dragStart = pos;
            ResetCrop();
            _cropPreview = new Rectangle
            {
                Stroke = new SolidColorBrush(Colors.Red),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(0x22, 0x1A, 0x6B, 0x8A)),
            };
            ShapeCanvas.Children.Add(_cropPreview);
            UpdateCropPreview(pos);
            ShapeCanvas.CaptureMouse();
        }
        else if (_mode == AnnotateMode.Text)
        {
            StartTextEditor(pos);
        }
    }

    private void OnShapeCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var pos = e.GetPosition(ShapeCanvas);
        if (_mode == AnnotateMode.Arrow)
            UpdateArrow(pos);
        else if (_mode == AnnotateMode.Crop)
            UpdateCropPreview(pos);
    }

    private void OnShapeCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ShapeCanvas.ReleaseMouseCapture();
        var pos = e.GetPosition(ShapeCanvas);

        if (_mode == AnnotateMode.Arrow && _previewLine is not null && _previewHead is not null)
        {
            // 拖动距离过近视为误触，撤销
            if ((pos - _dragStart).Length < 6)
            {
                ShapeCanvas.Children.Remove(_previewLine);
                ShapeCanvas.Children.Remove(_previewHead);
            }
            _previewLine = null;
            _previewHead = null;
        }
        else if (_mode == AnnotateMode.Crop && _cropPreview is not null)
        {
            var rect = NormalizeRect(_dragStart, pos);
            ShapeCanvas.Children.Remove(_cropPreview);
            _cropPreview = null;
            if (rect.Width >= 8 && rect.Height >= 8)
            {
                _cropRect = rect;
                AddCropBorder(rect);
                BtnConfirmCrop.Visibility = Visibility.Visible;
                HintText.Text = "已框选裁剪区（红框），点击「确认裁剪」应用；重新拖动可重选";
            }
        }
    }

    /// <summary>更新箭头几何：线段指向箭头底部，头部为等腰三角。</summary>
    private void UpdateArrow(Point end)
    {
        if (_previewLine is null || _previewHead is null) return;
        var start = _dragStart;
        var dir = end - start;
        var len = dir.Length;
        dir = len < 0.001 ? new Vector(1, 0) : dir / len;
        var headLen = Math.Max(10, _thickness * 4);
        var halfWidth = Math.Max(4, _thickness * 1.6);
        var basePoint = end - dir * headLen;
        var perp = new Vector(-dir.Y, dir.X);

        _previewLine.X1 = start.X;
        _previewLine.Y1 = start.Y;
        _previewLine.X2 = basePoint.X;
        _previewLine.Y2 = basePoint.Y;
        _previewHead.Points = new PointCollection
        {
            end,
            basePoint + perp * halfWidth,
            basePoint - perp * halfWidth,
        };
    }

    private void UpdateCropPreview(Point pos)
    {
        if (_cropPreview is null) return;
        var rect = NormalizeRect(_dragStart, pos);
        _cropPreview.Width = rect.Width;
        _cropPreview.Height = rect.Height;
        Canvas.SetLeft(_cropPreview, rect.X);
        Canvas.SetTop(_cropPreview, rect.Y);
    }

    private static Rect NormalizeRect(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new Rect(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    // --- 裁剪 ---

    /// <summary>在画布上叠加裁剪边框（红框）。</summary>
    private void AddCropBorder(Rect rect)
    {
        var border = new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Stroke = new SolidColorBrush(Colors.Red),
            StrokeThickness = 2,
        };
        Canvas.SetLeft(border, rect.X);
        Canvas.SetTop(border, rect.Y);
        ShapeCanvas.Children.Add(border);
        _cropElements.Add(border);
    }

    /// <summary>确认裁剪：暗化裁剪区外，最终保存时以裁剪区为界输出。</summary>
    private void OnConfirmCrop(object sender, RoutedEventArgs e)
    {
        if (_cropRect is not { } rect) return;
        BtnConfirmCrop.Visibility = Visibility.Collapsed;
        AddDimOverlay(rect);
        HintText.Text = "裁剪区已确认（红框为界），点击「保存为副本」输出裁剪结果";
    }

    /// <summary>用四块半透明矩形暗化裁剪区外。</summary>
    private void AddDimOverlay(Rect rect)
    {
        double w = CanvasGrid.Width, h = CanvasGrid.Height;
        var fill = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00));
        void Add(double x, double y, double width, double height)
        {
            if (width <= 0 || height <= 0) return;
            var r = new Rectangle { Width = width, Height = height, Fill = fill };
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            ShapeCanvas.Children.Add(r);
            _cropElements.Add(r);
        }
        Add(0, 0, w, rect.Y);                                  // 上
        Add(0, rect.Bottom, w, h - rect.Bottom);               // 下
        Add(0, rect.Y, rect.X, rect.Height);                   // 左
        Add(rect.Right, rect.Y, w - rect.Right, rect.Height);  // 右
    }

    /// <summary>清空裁剪状态（重新拖动时调用）。</summary>
    private void ResetCrop()
    {
        _cropRect = null;
        BtnConfirmCrop.Visibility = Visibility.Collapsed;
        foreach (var element in _cropElements)
            ShapeCanvas.Children.Remove(element);
        _cropElements.Clear();
    }

    // --- 文字 ---

    /// <summary>在点击位置弹出小输入框；Enter 提交为 TextBlock，Esc 取消。</summary>
    private void StartTextEditor(Point pos)
    {
        var editor = new TextBox
        {
            MinWidth = 140,
            FontSize = 16,
            Padding = new Thickness(4, 2, 4, 2),
            Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x6B, 0x8A)),
        };
        var host = new Border
        {
            Child = editor,
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1A, 0x1A, 0x1A)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4),
        };
        Canvas.SetLeft(host, pos.X);
        Canvas.SetTop(host, pos.Y);
        ShapeCanvas.Children.Add(host);
        editor.Focus();

        editor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitText(host, editor, pos);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ShapeCanvas.Children.Remove(host);
                e.Handled = true;
            }
        };
        editor.LostFocus += (_, _) =>
        {
            if (ShapeCanvas.Children.Contains(host))
                CommitText(host, editor, pos);
        };
    }

    private void CommitText(Border host, TextBox editor, Point pos)
    {
        ShapeCanvas.Children.Remove(host);
        var text = editor.Text.Trim();
        if (text.Length == 0) return;
        // 字号随图像尺寸缩放（小图用小字号），保证大图上文字不至于过小
        var fontSize = Math.Max(16, _bitmap.PixelHeight / 25.0);
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(_color),
            Effect = new DropShadowEffect { BlurRadius = 2, ShadowDepth = 1, Opacity = 0.8 },
        };
        Canvas.SetLeft(block, pos.X);
        Canvas.SetTop(block, pos.Y);
        ShapeCanvas.Children.Add(block);
    }

    // --- 保存 / 取消 ---

    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            SavedPath = RenderComposite();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存标注失败：{ex.Message}", "标注编辑",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>
    /// 渲染标注层合成（底图 + 笔迹 + 箭头 + 文字 + 暗化层），
    /// 若有裁剪区则以裁剪区为界（CroppedBitmap），编码 PNG 保存为副本。
    /// </summary>
    private string RenderComposite()
    {
        var pixelWidth = (int)Math.Round(CanvasGrid.Width);
        var pixelHeight = (int)Math.Round(CanvasGrid.Height);
        var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(CanvasGrid);

        BitmapSource final = rtb;
        if (_cropRect is { } rect)
        {
            // 裁剪区钳制到图像边界内
            var x = (int)Math.Clamp(Math.Floor(rect.X), 0, pixelWidth - 1);
            var y = (int)Math.Clamp(Math.Floor(rect.Y), 0, pixelHeight - 1);
            var width = (int)Math.Clamp(Math.Ceiling(rect.Width), 1, pixelWidth - x);
            var height = (int)Math.Clamp(Math.Ceiling(rect.Height), 1, pixelHeight - y);
            final = new CroppedBitmap(rtb, new Int32Rect(x, y, width, height));
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(final));
        var outputPath = BuildAnnotatedPath();
        using (var stream = File.Create(outputPath))
        {
            encoder.Save(stream);
        }
        return outputPath;
    }

    /// <summary>副本路径：同目录 原名_annotated.png；已存在则追加序号。</summary>
    private string BuildAnnotatedPath()
    {
        var dir = System.IO.Path.GetDirectoryName(_entry.OutputPath) ?? ".";
        var name = System.IO.Path.GetFileNameWithoutExtension(_entry.OutputPath);
        var candidate = System.IO.Path.Combine(dir, $"{name}_annotated.png");
        for (int i = 2; i < 1000 && File.Exists(candidate); i++)
            candidate = System.IO.Path.Combine(dir, $"{name}_annotated_{i}.png");
        return candidate;
    }
}
