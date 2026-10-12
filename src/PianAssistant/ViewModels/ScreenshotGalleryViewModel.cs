using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using PianAssistant.Helpers;
using PianAssistant.Models;

namespace PianAssistant.ViewModels;

/// <summary>
/// Gallery of captured screenshots. Each entry holds the output path
/// and the source position. "Open in Explorer" selects the file in Explorer.
/// </summary>
public sealed class ScreenshotGalleryViewModel : ObservableObject
{
    private ScreenshotEntry? _selected;

    public ObservableCollection<ScreenshotEntry> Items { get; } = new();

    public ScreenshotEntry? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public ICommand OpenInExplorerCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand ClearCommand { get; }

    public ScreenshotGalleryViewModel()
    {
        OpenInExplorerCommand = new RelayCommand(
            OpenInExplorer,
            () => _selected is not null);
        RemoveCommand = new RelayCommand(
            () => { if (_selected is not null) Items.Remove(_selected); },
            () => _selected is not null);
        ClearCommand = new RelayCommand(Clear);
    }

    public void Add(ScreenshotEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Items.Add(entry);
        Selected = entry;
    }

    public void Remove(ScreenshotEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Items.Remove(entry);
    }

    public void Clear()
    {
        Items.Clear();
        Selected = null;
    }

    private void OpenInExplorer()
    {
        if (_selected is null) return;
        var path = _selected.OutputPath;
        try
        {
            // /select opens Explorer with the file pre-selected (like Explorer right-click).
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ScreenshotGallery] OpenInExplorer failed: {ex.Message}");
        }
    }
}
