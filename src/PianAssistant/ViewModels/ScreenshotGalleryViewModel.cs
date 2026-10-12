using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using PianAssistant.Helpers;
using PianAssistant.Models;

namespace PianAssistant.ViewModels;

/// <summary>
/// Gallery of captured screenshots. Each entry holds the output path
/// and the source position. "Open in Explorer" selects the file in Explorer.
/// 支持按 Label（含 #标签 形式）与截图日期范围过滤（FilteredItems）。
/// </summary>
public sealed class ScreenshotGalleryViewModel : ObservableObject
{
    private ScreenshotEntry? _selected;
    private string _searchText = string.Empty;
    private DateTime? _filterStart;
    private DateTime? _filterEnd;
    private IReadOnlyList<ScreenshotEntry> _filteredItems = Array.Empty<ScreenshotEntry>();

    public ObservableCollection<ScreenshotEntry> Items { get; } = new();

    /// <summary>按搜索文本与日期范围过滤后的截图（供画廊 UI 绑定）。</summary>
    public IReadOnlyList<ScreenshotEntry> FilteredItems => _filteredItems;

    /// <summary>搜索文本：匹配 Label 子串（忽略大小写），支持 #标签 形式。</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                RefreshFilter();
        }
    }

    /// <summary>日期范围筛选起点（null=不限），按 CapturedAt 的日期比较。</summary>
    public DateTime? FilterStart
    {
        get => _filterStart;
        set
        {
            if (SetProperty(ref _filterStart, value))
                RefreshFilter();
        }
    }

    /// <summary>日期范围筛选终点（null=不限），按 CapturedAt 的日期比较。</summary>
    public DateTime? FilterEnd
    {
        get => _filterEnd;
        set
        {
            if (SetProperty(ref _filterEnd, value))
                RefreshFilter();
        }
    }

    public ScreenshotEntry? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    /// <summary>标注请求：View 层打开标注编辑器（当前选中项）。</summary>
    public event EventHandler<ScreenshotEntry>? AnnotateRequested;

    public ICommand OpenInExplorerCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand AnnotateCommand { get; }

    public ScreenshotGalleryViewModel()
    {
        // 集合任何变化都同步刷新过滤结果
        Items.CollectionChanged += (_, _) => RefreshFilter();
        RefreshFilter();

        OpenInExplorerCommand = new RelayCommand(
            OpenInExplorer,
            () => _selected is not null);
        RemoveCommand = new RelayCommand(
            () => { if (_selected is not null) Items.Remove(_selected); },
            () => _selected is not null);
        ClearCommand = new RelayCommand(Clear);
        AnnotateCommand = new RelayCommand(
            () => { if (_selected is { } entry) AnnotateRequested?.Invoke(this, entry); },
            () => _selected is not null);
    }

    public void Add(ScreenshotEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Items.Add(entry);
        Selected = entry;
    }

    /// <summary>
    /// 标注保存后把新副本加回画廊：Position 与原图相同，
    /// Label 加「(标注)」后缀；不修改原图条目。
    /// </summary>
    public ScreenshotEntry AddAnnotatedCopy(ScreenshotEntry source, string annotatedPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(annotatedPath);
        var label = string.IsNullOrEmpty(source.Label) ? "(标注)" : $"{source.Label}(标注)";
        var entry = new ScreenshotEntry(source.SourcePath, annotatedPath,
            source.Position, source.CapturedAt, label);
        Items.Add(entry);
        Selected = entry;
        return entry;
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

    /// <summary>在资源管理器中定位指定截图文件。</summary>
    public void OpenInExplorer(ScreenshotEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        RevealInExplorer(entry.OutputPath);
    }

    private void RefreshFilter()
    {
        IEnumerable<ScreenshotEntry> query = Items;
        var text = _searchText?.Trim();
        if (!string.IsNullOrEmpty(text))
            query = query.Where(s =>
                s.Label is not null && s.Label.Contains(text, StringComparison.OrdinalIgnoreCase));
        if (_filterStart is { } start)
            query = query.Where(s => s.CapturedAt.Date >= start.Date);
        if (_filterEnd is { } end)
            query = query.Where(s => s.CapturedAt.Date <= end.Date);
        _filteredItems = query.ToList();
        OnPropertyChanged(nameof(FilteredItems));
    }

    private void OpenInExplorer()
    {
        if (_selected is null) return;
        RevealInExplorer(_selected.OutputPath);
    }

    private static void RevealInExplorer(string path)
    {
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
