using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using PianAssistant.Models;
using PianAssistant.Services;
using PianAssistant.ViewModels;

namespace PianAssistant.Views;

public partial class NotesView : UserControl
{
    private static readonly SolidColorBrush ActiveTabBrush =
        new(Color.FromRgb(0xE8, 0xE8, 0xE8));
    private static readonly SolidColorBrush InactiveTabBrush =
        new(Color.FromRgb(0x88, 0x88, 0x88));

    private NotesViewModel? Vm => DataContext as NotesViewModel;

    public NotesView()
    {
        InitializeComponent();
        // DataContext 绑定在 MainWindow 中设置，这里订阅导出请求（View 弹文件对话框）
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is NotesViewModel oldVm) oldVm.ExportRequested -= OnExportRequested;
        if (e.NewValue is NotesViewModel newVm) newVm.ExportRequested += OnExportRequested;
    }

    // --- 子标签切换 ---

    private void OnShowScreenshots(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.ActiveSubTab = NotesSubTab.Screenshots;
        ScreenshotsPanel.Visibility = Visibility.Visible;
        SegmentsPanel.Visibility = Visibility.Collapsed;
        ActivitiesPanel.Visibility = Visibility.Collapsed;
        SetActiveTab(BtnScreenshots);
    }

    private void OnShowSegments(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.ActiveSubTab = NotesSubTab.Segments;
        ScreenshotsPanel.Visibility = Visibility.Collapsed;
        SegmentsPanel.Visibility = Visibility.Visible;
        ActivitiesPanel.Visibility = Visibility.Collapsed;
        SetActiveTab(BtnSegments);
    }

    private void OnShowActivities(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.ActiveSubTab = NotesSubTab.Activities;
        ScreenshotsPanel.Visibility = Visibility.Collapsed;
        SegmentsPanel.Visibility = Visibility.Collapsed;
        ActivitiesPanel.Visibility = Visibility.Visible;
        SetActiveTab(BtnActivities);
    }

    private void SetActiveTab(Button active)
    {
        foreach (var b in new[] { BtnScreenshots, BtnSegments, BtnActivities })
            b.Foreground = ReferenceEquals(b, active) ? ActiveTabBrush : InactiveTabBrush;
    }

    // --- 点击跳转 ---

    private void OnScreenshotClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ScreenshotEntry entry && Vm is not null)
            Vm.JumpTo(entry.Position);
    }

    // --- 截图右键菜单（画廊入口/标注/删除）---

    /// <summary>打开截图画廊窗口。</summary>
    private void OnOpenGallery(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.OpenGallery();
    }

    /// <summary>在资源管理器中定位截图文件。</summary>
    private void OnScreenshotOpenFolder(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ScreenshotEntry entry &&
            Window.GetWindow(this) is MainWindow main)
            main.Shell?.Screenshots.OpenInExplorer(entry);
    }

    /// <summary>打开标注编辑器（保存后副本自动加回画廊）。</summary>
    private void OnScreenshotAnnotate(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ScreenshotEntry entry &&
            Window.GetWindow(this) is MainWindow main)
            main.OpenAnnotation(entry);
    }

    /// <summary>从画廊与笔记面板删除该截图。</summary>
    private void OnScreenshotDelete(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ScreenshotEntry entry &&
            Window.GetWindow(this) is MainWindow main)
            main.RemoveScreenshot(entry);
    }

    // --- 片段面板：跳到批量导出面板 ---

    private void OnOpenClipsPanel(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.ShowClipsPanel();
    }

    private void OnSegmentClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TimelineSegment seg && Vm is not null)
            Vm.JumpTo(seg.Start);
    }

    private void OnActivityClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ActivityEntry entry &&
            Vm is not null && !entry.IsEditing)
            Vm.JumpTo(entry.Position);
    }

    // --- 活动条目：右键菜单 编辑/删除 ---

    private void OnActivityEdit(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ActivityEntry entry)
            entry.IsEditing = true;
    }

    private void OnActivityDelete(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is ActivityEntry entry)
            Vm?.Remove(entry);
    }

    // --- 活动条目：内联编辑 ---

    /// <summary>编辑框可见时聚焦并全选，便于直接输入。</summary>
    private void OnActivityEditBoxVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.IsVisible)
            tb.Dispatcher.InvokeAsync(() =>
            {
                tb.Focus();
                tb.SelectAll();
            }, DispatcherPriority.Input);
    }

    private void OnActivityEditKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not ActivityEntry entry) return;
        if (e.Key == Key.Enter)
        {
            CommitNoteEdit(tb, entry);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // 取消：绑定是 Explicit 更新，Label 未被修改，直接退出编辑态
            entry.IsEditing = false;
            e.Handled = true;
        }
    }

    private void OnActivityEditLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is ActivityEntry entry && entry.IsEditing)
            CommitNoteEdit(tb, entry);
    }

    /// <summary>提交笔记编辑：Explicit 更新源后交给 VM 重命名。</summary>
    private void CommitNoteEdit(TextBox tb, ActivityEntry entry)
    {
        tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        Vm?.RenameNote(entry, entry.Label); // 内部会退出编辑态并刷新过滤
    }

    // --- 笔记输入 ---

    private void OnAddNote(object sender, RoutedEventArgs e) => Vm?.AddNote(null);

    private void OnNoteInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Vm?.AddNote(null);
            e.Handled = true;
        }
    }

    // --- 搜索与时间范围（文本在 VM 过滤，这里只解析时间输入） ---

    private void OnRangeStartChanged(object sender, TextChangedEventArgs e)
    {
        if (Vm is not null)
            Vm.FilterStart = TryParseTime(RangeStartInput.Text);
    }

    private void OnRangeEndChanged(object sender, TextChangedEventArgs e)
    {
        if (Vm is not null)
            Vm.FilterEnd = TryParseTime(RangeEndInput.Text);
    }

    /// <summary>解析 mm:ss / hh:mm:ss 时间文本；空或无效返回 null（不限）。</summary>
    private static TimeSpan? TryParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return TimeSpan.TryParse(text.Trim(), CultureInfo.InvariantCulture, out var ts) && ts >= TimeSpan.Zero
            ? ts
            : null;
    }

    // --- 导出段（单段，M2 后续接入 ffmpeg） ---

    private void OnSegmentExport(object sender, RoutedEventArgs e)
    {
        // 导出单个段（通过 ShellVM 或直接调用 PlayerVM）
        if (sender is Button btn && btn.DataContext is TimelineSegment seg)
        {
            // TODO: 调用 PlayerVM 导出指定段
            System.Diagnostics.Debug.WriteLine($"[NotesView] Export segment #{seg.Index}: {seg.StartText}→{seg.EndText}");
        }
    }

    // --- 活动流导出（Markdown/TXT，按扩展名） ---

    private void OnExportRequested(object? sender, EventArgs e)
    {
        if (Vm is null) return;
        var dlg = new SaveFileDialog
        {
            Title = "导出活动流",
            FileName = "拉片笔记",
            Filter = "Markdown 文件|*.md|文本文件|*.txt",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            NotesExportService.Export(Vm.FilteredActivities, dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "导出活动流",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
