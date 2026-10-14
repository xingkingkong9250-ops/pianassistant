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
        Vm.BrowseDataRootRequested += OnBrowseDataRoot;
        Vm.CloseRequested += OnCloseRequested;
        Vm.DataRootChanged += OnDataRootChanged;
        Vm.ValidationFailed += OnValidationFailed;
    }

    private void OnBrowseDataRoot(object? sender, System.EventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "选择数据存放位置",
        };
        if (Vm is not null && dlg.ShowDialog() == true)
            Vm.SetDataRoot(dlg.FolderName);
    }

    /// <summary>数据路径实际变化 → 提示需重启生效（不搬迁任何文件）。</summary>
    private void OnDataRootChanged(object? sender, System.EventArgs e)
    {
        MessageBox.Show(this,
            "数据路径已修改，需重启应用后生效。\n（旧数据不会自动搬迁，请自行迁移或继续使用原路径。）",
            "需要重启", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnValidationFailed(object? sender, string message)
    {
        MessageBox.Show(this, message, "数据路径无效",
            MessageBoxButton.OK, MessageBoxImage.Warning);
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
