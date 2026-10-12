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

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        // 方向键不通过 KeyBinding 处理，而是通过 PreviewKeyDown/PreviewKeyUp
        // 实现长按连续触发
        PreviewKeyDown += OnPreviewArrowKeyDown;
        PreviewKeyUp += OnPreviewArrowKeyUp;
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
