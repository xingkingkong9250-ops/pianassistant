using System.Windows;
using System.Windows.Controls;
using PianAssistant.Models;
using PianAssistant.ViewModels;

namespace PianAssistant.Views;

public partial class PlaylistView : UserControl
{
    private PlaylistViewModel? Vm => DataContext as PlaylistViewModel;

    public PlaylistView()
    {
        InitializeComponent();
    }

    private void OnItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is MediaFile media && Vm is not null)
        {
            Vm.Select(media);
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (Vm is null) return;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var f in files!) Vm.AddPath(f);
        }
    }
}
