using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PianAssistant.Models;
using PianAssistant.ViewModels;

namespace PianAssistant.Views;

public partial class PlaylistView : UserControl
{
    private PlaylistViewModel? Vm => DataContext as PlaylistViewModel;

    // 拖拽排序：记录鼠标按下时的项索引与位置
    private int _dragStartIndex = -1;
    private Point _dragStartPoint;
    private static readonly Size DragThreshold = new(4, 4);

    public PlaylistView()
    {
        InitializeComponent();
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is MediaFile media && Vm is not null)
        {
            Vm.Select(media);
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (Vm is null) return;
        // 排序拖拽由 ListBox 的 OnListDrop 处理，这里仅处理外部文件拖入
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var f in files!) Vm.AddPath(f);
        }
    }

    // --- 拖拽重排序 ---

    private void OnListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 记录拖拽起点
        _dragStartPoint = e.GetPosition(null);
        _dragStartIndex = IndexFromPoint(List, e.GetPosition(List));
    }

    private void OnListPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (_dragStartIndex < 0) return;

        var pos = e.GetPosition(null);
        // 超过阈值才启动拖拽，避免误触
        if (Math.Abs(pos.X - _dragStartPoint.X) < DragThreshold.Width &&
            Math.Abs(pos.Y - _dragStartPoint.Y) < DragThreshold.Height)
            return;

        // 启动拖拽，数据为源索引
        var data = new DataObject(typeof(int), _dragStartIndex);
        DragDrop.DoDragDrop(List, data, DragDropEffects.Move);
        _dragStartIndex = -1;
        e.Handled = true;
    }

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(int)))
            e.Effects = DragDropEffects.Move;
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnListDrop(object sender, DragEventArgs e)
    {
        if (Vm is null) return;
        if (!e.Data.GetDataPresent(typeof(int))) return;
        var oldIndex = (int)e.Data.GetData(typeof(int))!;
        var newIndex = IndexFromPoint(List, e.GetPosition(List));
        if (newIndex < 0) newIndex = Vm.Items.Count - 1;
        if (newIndex >= 0)
            Vm.MoveItem(oldIndex, newIndex);
        e.Handled = true;
    }

    /// <summary>根据坐标取得 ListBox 项索引；命中空白区域返回 -1。</summary>
    private static int IndexFromPoint(ListBox lb, Point p)
    {
        for (int i = 0; i < lb.Items.Count; i++)
        {
            if (lb.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
            {
                var rect = new Rect(item.TransformToAncestor(lb).Transform(new Point(0, 0)),
                                    item.RenderSize);
                if (rect.Contains(p)) return i;
            }
        }
        return -1;
    }

    // --- 右键菜单 ---

    private void OnContextMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        if (sender is not MenuItem mi) return;
        // ContextMenu 的 PlacementTarget 是 ListBoxItem，DataContext 为 MediaFile
        var media = mi.DataContext as MediaFile;
        if (media is null) return;
        var index = Vm.Items.IndexOf(media);
        if (index < 0) return;

        switch (mi.Tag as string)
        {
            case "Delete":
                Vm.RemoveAt(index);
                break;
            case "CopyPath":
                try { Clipboard.SetText(media.Path); } catch { }
                break;
            case "OpenInExplorer":
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe",
                        $"/select,\"{media.Path}\"");
                }
                catch { }
                break;
        }
    }
}
