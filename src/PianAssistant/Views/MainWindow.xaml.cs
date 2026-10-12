using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LibVLCSharp.Shared;
using PianAssistant.Services;
using PianAssistant.ViewModels;
using PianAssistant.Views.Controls;

namespace PianAssistant.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Attach the LibVLC MediaPlayer to the VideoView control.
        var shell = Shell;
        if (shell is null) return;
        var mp = shell.Services.Playback as VideoPlaybackService;
        if (mp is not null)
        {
            VideoView.MediaPlayer = mp.LibVlcMediaPlayer;
        }
    }

    /// <summary>Timeline seek: convert 0..1 ratio to a seek call.</summary>
    private void OnTimelineSeek(object sender, double e)
    {
        Shell?.Player.SeekPercent(e);
    }

    /// <summary>Timeline segment selected: user clicked between cut points.</summary>
    private void OnTimelineSegmentSelected(object sender, TimeSpan e)
    {
        Shell?.Player.SelectSegmentAt(e);
    }

    /// <summary>缩放变化时更新标签。</summary>
    private void OnTimelineScaleChanged(object sender, double e)
    {
        if (ZoomLabel is not null)
            ZoomLabel.Text = $"{e:F1}×";
    }

    /// <summary>ScrollViewer 尺寸变化时更新 TimelineControl 的 viewport 宽度。</summary>
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
