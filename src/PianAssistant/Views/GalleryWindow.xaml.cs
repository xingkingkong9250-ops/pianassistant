using System.Windows;
using System.Windows.Input;
using PianAssistant.Models;
using PianAssistant.ViewModels;

namespace PianAssistant.Views;

/// <summary>
/// 截图画廊窗口：网格浏览缩略图，双击进入大图模式；
/// 大图模式下 ←/→ 翻页、Esc 返回网格（网格模式 Esc 关闭窗口）。
/// 右键菜单：打开所在文件夹 / 标注 / 删除。
/// </summary>
public partial class GalleryWindow : Window
{
    private GalleryViewModel? Vm => DataContext as GalleryViewModel;

    public GalleryWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (Vm is not null)
                Vm.AnnotateRequested += (_, entry) => OpenAnnotation(entry);
        };
        PreviewKeyDown += OnWindowKeyDown;
    }

    protected override void OnClosed(EventArgs e)
    {
        // 关闭时释放画廊 VM（退订底层集合事件，避免事件泄漏）
        (DataContext as GalleryViewModel)?.Dispose();
        base.OnClosed(e);
    }

    // --- 键盘导航 ---

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm is null) return;
        if (vm.IsViewerMode)
        {
            switch (e.Key)
            {
                case Key.Left:
                    vm.MovePrevious();
                    e.Handled = true;
                    break;
                case Key.Right:
                    vm.MoveNext();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    vm.IsViewerMode = false;
                    e.Handled = true;
                    break;
            }
        }
        else if (e.Key == Key.Escape)
        {
            // 网格模式 Esc 关闭画廊
            Close();
            e.Handled = true;
        }
    }

    // --- 网格双击进入大图 ---

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null) return;
        if (Vm.Current is not null)
            Vm.IsViewerMode = true;
    }

    // --- 大图模式右键返回网格 ---

    private void OnViewerRightClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not null)
            Vm.IsViewerMode = false;
    }

    // --- 右键菜单（网格项）---

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ScreenshotEntry entry &&
            Vm is not null)
            Vm.Gallery.OpenInExplorer(entry);
    }

    private void OnAnnotate(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ScreenshotEntry entry)
            OpenAnnotation(entry);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ScreenshotEntry entry &&
            Vm is not null)
            Vm.Gallery.Remove(entry);
    }

    /// <summary>
    /// 打开标注编辑器；保存成功后把标注副本加回画廊
    /// （Position 同原图，Label 加「(标注)」后缀）。
    /// </summary>
    private void OpenAnnotation(ScreenshotEntry entry)
    {
        var vm = Vm;
        if (vm is null) return;
        var win = new AnnotationWindow(entry) { Owner = this };
        if (win.ShowDialog() == true && win.SavedPath is not null)
            vm.Gallery.AddAnnotatedCopy(entry, win.SavedPath);
    }
}
