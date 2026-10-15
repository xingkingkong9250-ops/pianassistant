using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using PianAssistant.Interop;
using PianAssistant.Models;
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

    // 全局热键（可选开关：设置中启用后，应用失焦时按 S 截图）
    private GlobalHotkeyService? _globalHotkeys;

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

    // 截图画廊窗口（单例：已打开则聚焦）
    private GalleryWindow? _galleryWindow;

    // 右下角提示条自动隐藏定时器（M7）
    private DispatcherTimer? _toastTimer;

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
        // 输入法强制英文：焦点不在文本框时再次强制，避免中文 IME 吞掉单键快捷键
        IsKeyboardFocusWithinChanged += OnImeKeyboardFocusChanged;
    }

    // 供子视图（NotesView 等）访问 Shell；外部只读
    public ShellViewModel? Shell => DataContext as ShellViewModel;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // 挂 WndProc 钩子转发 WM_HOTKEY 给全局热键服务
        _hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);
        // 启动即强制英文输入模式，避免中文输入法吞掉单键快捷键
        ImeService.ForceEnglish(_hwnd);
    }

    // 主窗口句柄（IME 控制用）
    private IntPtr _hwnd;

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        // 从其它窗口切回时也强制一次（用户在外部切回中文后热键仍可用）
        ImeService.ForceEnglish(_hwnd);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // 退出时恢复进入程序前保存的输入法状态
        ImeService.RestoreSaved(_hwnd);
        base.OnClosing(e);
    }

    // 键盘焦点在窗口内且不在文本输入控件时强制英文模式；文本框内不打扰（可正常输入中文笔记）
    private void OnImeKeyboardFocusChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_hwnd == IntPtr.Zero || !IsKeyboardFocusWithin) return; // 失焦由 OnActivated 兜底
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
        ImeService.ForceEnglish(_hwnd);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Hotkey.WM_HOTKEY && _globalHotkeys?.HandleHotkey(wParam) == true)
            handled = true;
        return IntPtr.Zero;
    }

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

        // 截图画廊入口（顶部 🖼 按钮）
        shell.GalleryRequested += (_, _) => OpenGallery();

        // 项目文件入口（M6；IO/对话框在 View 层）
        shell.SaveProjectRequested += (_, _) => HandleSaveProject();
        shell.OpenProjectRequested += (_, _) => HandleOpenProjectPackage();
        shell.ExportProjectRequested += (_, _) => HandleExportProject();
        shell.ImportProjectRequested += (_, _) => HandleOpenProjectPackage();

        // 设置保存后重建键盘绑定（让自定义快捷键即时生效）
        shell.Settings.Saved += (_, _) => RebuildInputBindings();
        // 启动时按当前 HotkeyService 映射重建键盘绑定（替代原静态 XAML InputBindings）
        RebuildInputBindings();

        // 鼠标静止隐藏定时器
        _mouseHideTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = MouseHideDelay
        };
        _mouseHideTimer.Tick += OnMouseHideTimerTick;

        // 全局热键服务（按设置决定是否注册）
        if (_globalHotkeys is null)
        {
            _globalHotkeys = new GlobalHotkeyService();
            // WndProc 回调线程触发，切回 UI 线程执行截图命令
            _globalHotkeys.ScreenshotRequested += (_, _) =>
                Dispatcher.BeginInvoke(() => Shell?.Player.CaptureScreenshotCommand.Execute(null));
        }
        ApplyGlobalHotkeySetting();
    }

    /// <summary>
    /// 根据设置注册/注销全局热键（启动与设置窗口保存后调用）。
    /// 仅注册截图键（手势跟随应用内快捷键映射）；不注册 Space，理由见 GlobalHotkeyService。
    /// </summary>
    public void ApplyGlobalHotkeySetting()
    {
        if (_globalHotkeys is null) return;
        var enabled = Shell?.Services.Settings.GlobalHotkeysEnabled == true;
        if (!enabled)
        {
            _globalHotkeys.Unregister();
            return;
        }
        var hwnd = new WindowInteropHelper(this).Handle;
        var gesture = Shell!.Services.Hotkeys.Active[HotkeyAction.CaptureScreenshot];
        if (!_globalHotkeys.TryRegisterScreenshot(hwnd, gesture))
            System.Diagnostics.Debug.WriteLine("[MainWindow] 全局热键注册失败（可能被其他应用占用）");
    }

    // --- 动态键盘绑定（M6：让设置窗自定义快捷键真正生效）---

    /// <summary>
    /// 清空并按 <see cref="HotkeyService.Active"/> 当前映射重建键盘绑定。
    /// 启动时调用一次；设置窗保存后再次调用。手势字符串经 TryParseKeyModifiers
    /// 解析为 (Key, ModifierKeys)，再以直接赋值方式写入 KeyBinding——这样可支持
    /// WPF KeyGesture 无修饰键时拒绝的裸字母/数字键（S、F、D1~D9）。带参数的动作
    /// （D1~D9）携带 CommandParameter。
    /// </summary>
    private void RebuildInputBindings()
    {
        InputBindings.Clear();
        var shell = Shell;
        if (shell is null) return;

        foreach (var kv in shell.Services.Hotkeys.Active)
        {
            var binding = shell.ResolveHotkey(kv.Key);
            if (binding is null) continue;
            if (!HotkeyService.TryParseKeyModifiers(kv.Value, out var key, out var modifiers)) continue;
            InputBindings.Add(new KeyBinding
            {
                Command = binding.Value.Command,
                CommandParameter = binding.Value.Parameter,
                Key = key,
                Modifiers = modifiers,
            });
        }
    }

    // --- 项目文件（IO/对话框在 View 层，VM 返回 ProjectOperationResult）---

    private void HandleSaveProject()
    {
        var shell = Shell;
        if (shell is null) return;
        ShowProjectResult(shell.SaveCurrentProject(), "项目已保存到数据库。");
    }

    private void HandleOpenProjectPackage()
    {
        var shell = Shell;
        if (shell is null) return;
        var dlg = new OpenFileDialog
        {
            Title = "打开项目包",
            Filter = "拉片项目 (*.paproj)|*.paproj|所有文件 (*.*)|*.*",
            Multiselect = false,
        };
        if (dlg.ShowDialog(this) != true) return;
        ShowProjectResult(shell.ImportAndLoadProject(dlg.FileName), "项目已导入并加载。");
    }

    private void HandleExportProject()
    {
        var shell = Shell;
        if (shell is null) return;
        var dlg = new SaveFileDialog
        {
            Title = "打包导出项目",
            Filter = "拉片项目 (*.paproj)|*.paproj|所有文件 (*.*)|*.*",
            DefaultExt = ".paproj",
            FileName = "项目.paproj",
        };
        if (dlg.ShowDialog(this) != true) return;
        ShowProjectResult(shell.ExportCurrentProject(dlg.FileName), "项目已打包导出。");
    }

    /// <summary>统一展示项目操作结果：成功用信息框，失败用警告框。</summary>
    private void ShowProjectResult(ProjectOperationResult result, string successMessage)
    {
        if (result.Success)
            MessageBox.Show(this, successMessage, "项目", MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(this, result.Error ?? "项目操作失败。", "项目", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // --- 最近文件菜单（右键「打开文件」按钮）---

    private void OnRecentFilesMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        menu.Items.Clear();
        var shell = Shell;
        foreach (var item in RecentFilesPresenter.Build(shell?.Services.Settings.RecentFiles))
        {
            var menuItem = new MenuItem { Header = item.Header, ToolTip = item.ToolTip };
            if (item.Path is null)
            {
                menuItem.IsEnabled = false; // 空列表占位「（无最近文件）」
            }
            else
            {
                var path = item.Path;
                menuItem.Click += (_, _) => shell?.OpenMedia(path);
            }
            menu.Items.Add(menuItem);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // 窗口关闭时注销全局热键
        _globalHotkeys?.Dispose();
        _globalHotkeys = null;
        base.OnClosed(e);
    }

    /// <summary>
    /// 在右下角显示一条临时提示（M7 静默恢复等），数秒后自动隐藏，不弹窗打断操作。
    /// </summary>
    public void ShowToast(string message, int seconds = 5)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowToast(message, seconds));
            return;
        }
        ToastText.Text = message;
        ToastBorder.Visibility = Visibility.Visible;
        ToastBorder.Opacity = 1;
        if (_toastTimer is null)
        {
            _toastTimer = new DispatcherTimer(DispatcherPriority.Background);
            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer!.Stop();
                ToastBorder.Visibility = Visibility.Collapsed;
                ToastBorder.Opacity = 0;
            };
        }
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(seconds);
        _toastTimer.Start();
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

    // --- 截图画廊 / 标注（供 NotesView 等子视图调用）---

    /// <summary>打开截图画廊窗口（已打开则激活）。</summary>
    public void OpenGallery()
    {
        var shell = Shell;
        if (shell is null) return;
        if (_galleryWindow is { IsLoaded: true })
        {
            _galleryWindow.Activate();
            return;
        }
        _galleryWindow = new GalleryWindow
        {
            Owner = this,
            DataContext = new GalleryViewModel(shell.Screenshots),
        };
        _galleryWindow.Closed += (_, _) => _galleryWindow = null;
        _galleryWindow.Show();
    }

    /// <summary>打开截图标注编辑器；保存成功后把标注副本加回画廊。</summary>
    public void OpenAnnotation(ScreenshotEntry entry)
    {
        var shell = Shell;
        if (shell is null) return;
        var win = new AnnotationWindow(entry) { Owner = this };
        if (win.ShowDialog() == true && win.SavedPath is not null)
            shell.Screenshots.AddAnnotatedCopy(entry, win.SavedPath);
    }

    /// <summary>切换侧边栏到片段批量导出面板。</summary>
    public void ShowClipsPanel()
    {
        if (Shell is not null)
            Shell.ActivePanel = ViewModels.SidebarPanel.Clips;
    }

    /// <summary>从画廊与笔记面板同时移除一张截图。</summary>
    public void RemoveScreenshot(ScreenshotEntry entry)
    {
        var shell = Shell;
        if (shell is null) return;
        shell.Screenshots.Remove(entry);
        shell.Notes.RemoveScreenshot(entry);
    }

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
            var shell = Shell;
            var player = shell?.Player;
            if (player is not null)
            {
                var videos = new List<string>();
                foreach (var f in files)
                {
                    if (IsSubtitleFile(f))
                        player.LoadSubtitle(f);
                    else
                        videos.Add(f);
                }
                // 视频批量加入播放列表并播放第一个（多选拖放同 Windows 选择逻辑）
                if (videos.Count > 0)
                    shell?.OpenMedia(videos);
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

        // 带 Ctrl/Shift 的方向键交给 InputBindings（Shift+←/→ 1秒、Ctrl+←/→ 10秒、Ctrl+↑/↓ 音量），
        // 不参与长按帧步进，避免与额外绑定冲突。
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
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
