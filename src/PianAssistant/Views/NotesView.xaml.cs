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

    private void OnAddNote(object sender, System.Windows.RoutedEventArgs e)
    {
        Vm?.AddNote(Vm?.SelectedNote?.SourcePath);
    }

    private void OnNoteInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Vm?.AddNote(Vm?.SelectedNote?.SourcePath);
            e.Handled = true;
        }
    }

    private void OnNoteClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is NoteEntry note && Vm is not null)
        {
            Vm.JumpTo(note);
        }
    }
}
