using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PianAssistant.Models;
using PianAssistant.ViewModels;

namespace PianAssistant.Views;

public partial class NotesView : UserControl
{
    private NotesViewModel? Vm => DataContext as NotesViewModel;

    public NotesView()
    {
        InitializeComponent();
    }

    // --- 子标签切换 ---

    private void OnShowScreenshots(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.ActiveSubTab = NotesSubTab.Screenshots;
        ScreenshotsPanel.Visibility = Visibility.Visible;
        SegmentsPanel.Visibility = Visibility.Collapsed;
        BtnScreenshots.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xE8));
        BtnSegments.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
    }

    private void OnShowSegments(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.ActiveSubTab = NotesSubTab.Segments;
        ScreenshotsPanel.Visibility = Visibility.Collapsed;
        SegmentsPanel.Visibility = Visibility.Visible;
        BtnScreenshots.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
        BtnSegments.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xE8));
    }

    // --- 点击跳转 ---

    private void OnScreenshotClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ScreenshotEntry entry && Vm is not null)
            Vm.JumpTo(entry.Position);
    }

    private void OnSegmentClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TimelineSegment seg && Vm is not null)
            Vm.JumpTo(seg.Start);
    }

    // --- 导出段 ---

    private void OnSegmentExport(object sender, RoutedEventArgs e)
    {
        // 导出单个段（通过 ShellVM 或直接调用 PlayerVM）
        if (sender is Button btn && btn.DataContext is TimelineSegment seg)
        {
            // TODO: 调用 PlayerVM 导出指定段
            System.Diagnostics.Debug.WriteLine($"[NotesView] Export segment #{seg.Index}: {seg.StartText}→{seg.EndText}");
        }
    }
}
