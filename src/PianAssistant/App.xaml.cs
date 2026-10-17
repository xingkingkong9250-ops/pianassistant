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
    private MainWindow? _mainWindow;
    // 全局异常弹窗限频（防止异常风暴时嵌套模态消息泵栈溢出）。
    private DateTime _lastErrorBoxUtc = DateTime.MinValue;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 全局异常兜底：必须在任何业务逻辑前注册（含单实例守卫路径）。
        RegisterGlobalExceptionHandlers();

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

        // 数据库损坏自愈/失败时给用户明确提示（不静默丢数据）。
        if (_services.DatabaseStatus is { Message: not null } dbStatus)
        {
            MessageBox.Show(
                dbStatus.Message,
                dbStatus.Success ? "数据库已恢复" : "数据库不可用",
                MessageBoxButton.OK,
                dbStatus.Success ? MessageBoxImage.Warning : MessageBoxImage.Error);
        }

        _shell = new ShellViewModel(_services);

        // Wire view-level events.
        _shell.OpenFileRequested += (_, _) => OpenFileViaDialog();
        _shell.ExitRequested += (_, _) => Shutdown();
        _shell.SettingsRequested += (_, _) => ShowSettings();
        _shell.ShowBatchRequested += (_, _) => ShowBatch();
        // 视频缺失/打开失败/会话还原等提示：走右下角提示条，不弹窗打断（M7）。
        _shell.NotificationRequested += (_, msg) =>
        {
            if (_mainWindow is not null)
                _mainWindow.ShowToast(msg);
            else
                MessageBox.Show(msg, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        // 播放器操作失败（截图/截取片段等）提示
        _shell.Player.ErrorOccurred += (_, msg) =>
            MessageBox.Show(msg, "操作失败", MessageBoxButton.OK, MessageBoxImage.Warning);

        // Create main window manually (no StartupUri) so that DataContext
        // is set BEFORE Loaded fires — VideoView.MediaPlayer binding in
        // OnLoaded needs Shell to be available.
        _mainWindow = new MainWindow
        {
            DataContext = _shell
        };
        _mainWindow.Closing += (_, _) => _shell.Dispose();
        _mainWindow.Show();
    }

    private void OpenFileViaDialog()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择视频文件（Ctrl/Shift 可多选）",
            Filter = "视频文件|*.mp4;*.mkv;*.avi;*.mov;*.flv;*.wmv;*.webm;*.ts;*.m2ts;*.mpg|所有文件|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog() == true && _shell is not null)
        {
            _shell.OpenMedia(dlg.FileNames);
        }
    }

    private void ShowSettings()
    {
        if (_services is null || _shell is null) return;
        // 设置窗绑定 Shell.Settings（否则所有绑定落空）
        var win = new SettingsWindow { Owner = MainWindow, DataContext = _shell.Settings };
        win.ShowDialog();
        // 设置保存后应用全局热键开关（注册/注销在 MainWindow）
        (MainWindow as MainWindow)?.ApplyGlobalHotkeySetting();
    }

    /// <summary>打开批量文本操作窗口（绑定 Shell.Batch）。</summary>
    private void ShowBatch()
    {
        if (_shell is null) return;
        var win = new BatchWindow { Owner = MainWindow, DataContext = _shell.Batch };
        win.Show();
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

    /// <summary>
    /// 全局异常兜底：
    /// UI 线程未处理异常 → 记录日志 + 弹窗提示 + 标记已处理（不直接崩溃应用）；
    /// 后台线程异常与未观察的任务异常 → 仅记录日志（无法安全恢复 UI）。
    /// </summary>
    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("[App] 未处理的 UI 线程异常", args.Exception);
            // 尽力把当前素材会话落库，降低异常退出时的数据损失（M7）。
            TryFlushSessions();
            // 弹窗限频：异常风暴（如布局 NaN 循环）若每次异常都弹模态框，
            // 嵌套消息泵会栈溢出（TextShaping.dll 0xc00000fd）直接杀死进程。
            if ((DateTime.UtcNow - _lastErrorBoxUtc) > TimeSpan.FromSeconds(30))
            {
                _lastErrorBoxUtc = DateTime.UtcNow;
                MessageBox.Show(
                    "发生未处理错误，已记录日志。应用可能仍可继续使用。",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                AppLog.Error("[App] 未处理的后台线程异常", ex);
            else
                AppLog.Info($"[App] 未处理的后台异常：{args.ExceptionObject}");
            TryFlushSessions();
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("[App] 未观察的任务异常", args.Exception);
            args.SetObserved();
        };
    }

    /// <summary>退出/异常路径的尽力落库：把当前素材会话立即写入数据库（M7）。</summary>
    private void TryFlushSessions()
    {
        try { _services?.Sessions.EndSession(); }
        catch (Exception ex) { AppLog.Error("[App] 退出前会话落库失败", ex); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TryFlushSessions();
        _services?.Playback.Dispose();
        base.OnExit(e);
    }
}
