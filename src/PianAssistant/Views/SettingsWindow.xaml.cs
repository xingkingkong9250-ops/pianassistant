using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PianAssistant.ViewModels;

namespace PianAssistant.Views;

public partial class SettingsWindow : Window
{
    private SettingsViewModel? Vm => DataContext as SettingsViewModel;

    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.BrowseScreenshotsRequested += OnBrowseScreenshots;
        Vm.BrowseClipsRequested += OnBrowseClips;
        Vm.CloseRequested += OnCloseRequested;
    }

    private void OnBrowseScreenshots(object? sender, System.EventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "选择截图输出目录",
        };
        if (Vm is not null && dlg.ShowDialog() == true)
            Vm.SetScreenshotsDirectory(dlg.FolderName);
    }

    private void OnBrowseClips(object? sender, System.EventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "选择片段输出目录",
        };
        if (Vm is not null && dlg.ShowDialog() == true)
            Vm.SetClipsDirectory(dlg.FolderName);
    }

    private void OnApplyHotkey(object sender, RoutedEventArgs e)
    {
        Vm?.CommitHotkey();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Vm?.SaveCommand.Execute(null);
    }

    private void OnCloseRequested(object? sender, System.EventArgs e)
    {
        Close();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
    }
}
