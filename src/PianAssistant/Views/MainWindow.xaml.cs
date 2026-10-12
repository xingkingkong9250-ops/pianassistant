using System.Windows;
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
}
