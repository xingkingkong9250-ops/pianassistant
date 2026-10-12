using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using PianAssistant.Services;
using PianAssistant.ViewModels;
using PianAssistant.Views.Controls;

namespace PianAssistant.Views;

public partial class MainWindow : Window
{
    // 方向键长按检测
    private DispatcherTimer? _arrowRepeatTimer;
    private Key _currentArrowKey;
    private bool _isArrowRepeating;
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(50);

    // 全屏状态
    private bool _isFullscreen;
    // 保存非全屏时的行列尺寸，退出全屏时恢复
    private GridLength _origRow0Height = new(48);
    private GridLength _origRow2Height = GridLength.Auto;
    private GridLength _origRow3Height = new(56);
    private GridLength _origCol1Width = new(6);
    private GridLength _origCol2Width = new(300);
    private double _origCol2MinWidth = 200;
    private bool _savedOriginalSizes;

    // 全屏时鼠标自动隐藏
    private DispatcherTimer? _mouseHideTimer;
    private static readonly TimeSpan MouseHideDelay = TimeSpan.FromSeconds(3);

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        // 方向键不通过 KeyBinding 处理，而是通过 PreviewKeyDown/PreviewKeyUp
        // 实现长按连续触发
        PreviewKeyDown += OnPreviewArrowKeyDown;
        PreviewKeyUp += OnPreviewArrowKeyUp;
        // 全屏时鼠标静止自动隐藏
        PreviewMouseMove += OnWindowPreviewMouseMove;
    }

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var shell = Shell;
        if (shell is null) return;
        var mp = shell.Services.Playback as VideoPlaybackService;
        if (mp is not null)
        {
            VideoView.MediaPlayer = mp.LibVlcMediaPlayer;
        }
        // 订阅全屏切换/退出事件（由 PlayerViewModel 的 F / Esc 快捷键触发）
        shell.Player.FullscreenToggleRequested += OnFullscreenToggle;
        shell.Player.FullscreenExitRequested += OnFullscreenExit;

        // 鼠标静止隐藏定时器
        _mouseHideTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = MouseHideDelay
        };
        _mouseHideTimer.Tick += OnMouseHideTimerTick;
    }

    // --- 全屏模式 ---

    private void OnFullscreenToggle(object? sender, EventArgs e)
    {
        if (_isFullscreen) ExitFullscreen();
        else EnterFullscreen();
    }

    private void OnFullscreenExit(object? sender, EventArgs e)
    {
        if (_isFullscreen) ExitFullscreen();
    }

    private void SaveOriginalSizes()
    {
        if (_savedOriginalSizes) return;
        _savedOriginalSizes = true;
        _origRow0Height = RootGrid.RowDefinitions[0].Height;
        _origRow2Height = RootGrid.RowDefinitions[2].Height;
        _origRow3Height = RootGrid.RowDefinitions[3].Height;
        _origCol1Width = ContentGrid.ColumnDefinitions[1].Width;
        _origCol2Width = ContentGrid.ColumnDefinitions[2].Width;
        _origCol2MinWidth = ContentGrid.ColumnDefinitions[2].MinWidth;
    }

    private void EnterFullscreen()
    {
        if (_isFullscreen) return;
        _isFullscreen = true;
        SaveOriginalSizes();

        // 先改样式再最大化，减少闪烁
        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Maximized;
        Topmost = true;

        // 折叠顶部导航、时间轴、底部控制栏
        RootGrid.RowDefinitions[0].Height = new GridLength(0);
        RootGrid.RowDefinitions[2].Height = new GridLength(0);
        RootGrid.RowDefinitions[3].Height = new GridLength(0);
        TopNavBorder.Visibility = Visibility.Collapsed;
        TimelineBorder.Visibility = Visibility.Collapsed;
        BottomControlBorder.Visibility = Visibility.Collapsed;

        // 折叠侧边栏与分隔条
        ContentGrid.ColumnDefinitions[1].Width = new GridLength(0);
        ContentGrid.ColumnDefinitions[2].Width = new GridLength(0);
        ContentGrid.ColumnDefinitions[2].MinWidth = 0;
        SidebarSplitter.Visibility = Visibility.Collapsed;
        SidebarPanel.Visibility = Visibility.Collapsed;

        // 启动鼠标隐藏定时器
        _mouseHideTimer?.Start();
    }

    private void ExitFullscreen()
    {
        if (!_isFullscreen) return;
        _isFullscreen = false;

        // 停止鼠标隐藏定时器并恢复指针
        _mouseHideTimer?.Stop();
        Mouse.OverrideCursor = null;

        // 先恢复正常状态再恢复样式
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Topmost = false;

        // 恢复行列尺寸与可见性
        RootGrid.RowDefinitions[0].Height = _origRow0Height;
        RootGrid.RowDefinitions[2].Height = _origRow2Height;
        RootGrid.RowDefinitions[3].Height = _origRow3Height;
        TopNavBorder.Visibility = Visibility.Visible;
        TimelineBorder.Visibility = Visibility.Visible;
        BottomControlBorder.Visibility = Visibility.Visible;

        ContentGrid.ColumnDefinitions[1].Width = _origCol1Width;
        ContentGrid.ColumnDefinitions[2].Width = _origCol2Width;
        ContentGrid.ColumnDefinitions[2].MinWidth = _origCol2MinWidth;
        SidebarSplitter.Visibility = Visibility.Visible;
        SidebarPanel.Visibility = Visibility.Visible;
    }

    // --- 全屏鼠标自动隐藏 ---

    private void OnWindowPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isFullscreen) return;
        // 鼠标移动即恢复指针
        if (Mouse.OverrideCursor == Cursors.None)
            Mouse.OverrideCursor = null;
        // 重启静止计时器
        _mouseHideTimer?.Stop();
        _mouseHideTimer?.Start();
    }

    private void OnMouseHideTimerTick(object? sender, EventArgs e)
    {
        _mouseHideTimer?.Stop();
        if (_isFullscreen)
            Mouse.OverrideCursor = Cursors.None;
    }

    // --- 字幕拖拽到视频窗口 ---

    // 支持的字幕扩展名
    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".ass", ".ssa", ".vtt", ".sub"
    };

    private static bool IsSubtitleFile(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        return SubtitleExtensions.Contains(ext);
    }

    private void OnVideoDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            e.Effects = Array.Exists(files, IsSubtitleFile)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnVideoDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            var player = Shell?.Player;
            if (player is not null)
            {
                foreach (var f in files)
                {
                    if (IsSubtitleFile(f))
                        player.LoadSubtitle(f);
                }
            }
        }
        e.Handled = true;
    }

    // --- 方向键长按连续触发 ---

    private void OnPreviewArrowKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key;
        if (key != Key.Left && key != Key.Right && key != Key.Up && key != Key.Down)
            return;

        // 如果已有同方向的定时器在跑，忽略重复 KeyDown（系统自动重复）
        if (_arrowRepeatTimer is not null && _currentArrowKey == key)
        {
            e.Handled = true;
            return;
        }

        // 停止之前的定时器（方向切换）
        StopArrowTimer();

        _currentArrowKey = key;
        ExecuteArrowAction(key);

        // 启动初始延迟定时器（300ms 后切换为连续重复）
        _arrowRepeatTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = InitialDelay
        };
        _arrowRepeatTimer.Tick += (s, _) =>
        {
            if (_arrowRepeatTimer is null) return;
            if (!_isArrowRepeating)
            {
                _isArrowRepeating = true;
                _arrowRepeatTimer.Interval = RepeatInterval;
            }
            ExecuteArrowAction(_currentArrowKey);
        };
        _arrowRepeatTimer.Start();
        e.Handled = true;
    }

    private void OnPreviewArrowKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Left && e.Key != Key.Right && e.Key != Key.Up && e.Key != Key.Down)
            return;
        if (e.Key == _currentArrowKey)
            StopArrowTimer();
        e.Handled = true;
    }

    private void StopArrowTimer()
    {
        _arrowRepeatTimer?.Stop();
        _arrowRepeatTimer = null;
        _isArrowRepeating = false;
    }

    private void ExecuteArrowAction(Key key)
    {
        var player = Shell?.Player;
        if (player is null) return;
        switch (key)
        {
            case Key.Left:
                player.SeekBackwardCustomCommand.Execute(null);
                ShowArrowFeedback("◀", $"{GetSeekFramesText(player)}", "#4EC9B0");
                break;
            case Key.Right:
                player.SeekForwardCustomCommand.Execute(null);
                ShowArrowFeedback("▶", $"{GetSeekFramesText(player)}", "#4EC9B0");
                break;
            case Key.Up:
                player.IncreaseRateCustomCommand.Execute(null);
                ShowArrowFeedback("▲", $"{player.Rate:F2}×", "#569CD6");
                break;
            case Key.Down:
                player.DecreaseRateCustomCommand.Execute(null);
                ShowArrowFeedback("▼", $"{player.Rate:F2}×", "#569CD6");
                break;
        }
    }

    private static string GetSeekFramesText(PlayerViewModel player)
    {
        var frames = player.SeekStepFrames;
        var fps = Math.Max(player.Fps, 1.0);
        var seconds = frames / fps;
        if (frames == 1) return "1 帧";
        if (seconds >= 1.0) return $"{frames} 帧 ({seconds:F1}s)";
        return $"{frames} 帧";
    }

    // --- 视觉反馈 ---

    private DispatcherTimer? _feedbackHideTimer;
    private static readonly TimeSpan FeedbackDuration = TimeSpan.FromMilliseconds(600);

    private void ShowArrowFeedback(string icon, string text, string color)
    {
        if (ArrowFeedbackOverlay is null) return;

        ArrowFeedbackIcon.Text = icon;
        ArrowFeedbackText.Text = text;
        var c = (Color)ColorConverter.ConvertFromString(color);
        ArrowFeedbackText.Foreground = new SolidColorBrush(c);

        ArrowFeedbackOverlay.Visibility = Visibility.Visible;
        ArrowFeedbackOverlay.Opacity = 1;
        ArrowFeedbackScale.ScaleX = 1.15;
        ArrowFeedbackScale.ScaleY = 1.15;

        _feedbackHideTimer?.Stop();
        _feedbackHideTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = FeedbackDuration
        };
        _feedbackHideTimer.Tick += (s, _) =>
        {
            _feedbackHideTimer?.Stop();
            ArrowFeedbackOverlay.Opacity = 0;
            ArrowFeedbackScale.ScaleX = 0.85;
            ArrowFeedbackScale.ScaleY = 0.85;
        };
        _feedbackHideTimer.Start();
    }

    // --- Timeline ---

    private void OnTimelineSeek(object sender, double e)
    {
        Shell?.Player.SeekPercent(e);
    }

    private void OnTimelineSegmentSelected(object sender, TimeSpan e)
    {
        Shell?.Player.SelectSegmentAt(e);
    }

    private void OnTimelineScaleChanged(object sender, double e)
    {
        if (ZoomLabel is not null)
            ZoomLabel.Text = $"{e:F1}×";
    }

    private void OnTimelineScrollSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            var tc = FindTimelineControl(sv);
            if (tc is not null)
                tc.SetViewportWidth(sv.ViewportWidth);
        }
    }

    private TimelineControl? FindTimelineControl(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TimelineControl tc) return tc;
            var found = FindTimelineControl(child);
            if (found is not null) return found;
        }
        return null;
    }
}
