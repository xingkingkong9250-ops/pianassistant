using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using PianAssistant.Services;
using PianAssistant.ViewModels;
using PianAssistant.Views;

namespace PianAssistant;

public partial class App : Application
{
    private static readonly Mutex SingleInstanceMutex =
        new(false, "Global\\PianAssistant_SingleInstance");

    private ServiceRegistry? _services;
    private ShellViewModel? _shell;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Single-instance guard: request graceful shutdown of any prior instance.
        if (!SingleInstanceMutex.WaitOne(0, false))
        {
            TrySignalExistingInstance();
            Shutdown();
            return;
        }

        base.OnStartup(e);

        try
        {
            _services = ServiceRegistry.CreateDefault();
        }
        catch (FileNotFoundException ex)
        {
            MessageBox.Show(
                $"缺少必要的依赖文件：\n{ex.Message}\n\n请先运行 tools\\download-ffmpeg.ps1 下载 ffmpeg。",
                "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _shell = new ShellViewModel(_services);

        // Wire view-level events.
        _shell.OpenFileRequested += (_, _) => OpenFileViaDialog();
        _shell.ExitRequested += (_, _) => Shutdown();
        _shell.SettingsRequested += (_, _) => ShowSettings();

        // Main window.
        var mainWindow = MainWindow;
        if (mainWindow is not null)
        {
            mainWindow.DataContext = _shell;
            mainWindow.Closing += (_, _) => _shell.Dispose();
        }
    }

    private void OpenFileViaDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择视频文件",
            Filter = "视频文件|*.mp4;*.mkv;*.avi;*.mov;*.flv;*.wmv;*.webm;*.ts;*.m2ts;*.mpg|所有文件|*.*",
            Multiselect = false,
        };
        if (dlg.ShowDialog() == true && _shell is not null)
        {
            _shell.OpenMedia(dlg.FileName);
        }
    }

    private void ShowSettings()
    {
        if (_services is null) return;
        var win = new SettingsWindow { Owner = MainWindow };
        win.ShowDialog();
    }

    private static void TrySignalExistingInstance()
    {
        try
        {
            var grip = EventWaitHandle.OpenExisting("Local\\PianAssistant_GracefulShutdown");
            grip.Set();
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Playback.Dispose();
        base.OnExit(e);
    }
}
