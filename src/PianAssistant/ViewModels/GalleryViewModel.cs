using System.Windows.Input;
using PianAssistant.Helpers;
using PianAssistant.Models;

namespace PianAssistant.ViewModels;

/// <summary>
/// 截图画廊窗口 ViewModel：包装 <see cref="ScreenshotGalleryViewModel"/>，
/// 提供网格/大图两种浏览模式（双击进入大图、Esc 返回网格）、
/// 左右翻页、「第 N / M 张」计数与位置时间码、右键操作（打开所在文件夹/标注/删除）。
/// </summary>
public sealed class GalleryViewModel : ObservableObject
{
    private readonly ScreenshotGalleryViewModel _gallery;
    private ScreenshotEntry? _current;
    private bool _isViewerMode;

    public GalleryViewModel(ScreenshotGalleryViewModel gallery)
    {
        ArgumentNullException.ThrowIfNull(gallery);
        _gallery = gallery;
        // 过滤结果或选中变化时同步当前项
        _gallery.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ScreenshotGalleryViewModel.FilteredItems)
                or nameof(ScreenshotGalleryViewModel.Selected))
                RefreshCurrent();
        };
        RefreshCurrent();

        EnterViewerCommand = new RelayCommand(
            () => IsViewerMode = true,
            () => Current is not null);
        ExitViewerCommand = new RelayCommand(() => IsViewerMode = false);
        NextImageCommand = new RelayCommand(MoveNext, () => CurrentIndex < Count - 1);
        PreviousImageCommand = new RelayCommand(MovePrevious, () => CurrentIndex > 0);
        OpenInExplorerCommand = new RelayCommand(
            () => { if (Current is { } entry) _gallery.OpenInExplorer(entry); },
            () => Current is not null);
        AnnotateCommand = new RelayCommand(
            () => { if (Current is { } entry) AnnotateRequested?.Invoke(this, entry); },
            () => Current is not null);
        RemoveCommand = new RelayCommand(
            () => { if (Current is { } entry) _gallery.Remove(entry); },
            () => Current is not null);
    }

    /// <summary>底层截图集合 VM（搜索/过滤/标注副本都落回这里）。</summary>
    public ScreenshotGalleryViewModel Gallery => _gallery;

    /// <summary>过滤后的截图列表（画廊网格与大图数据源）。</summary>
    public IReadOnlyList<ScreenshotEntry> Items => _gallery.FilteredItems;

    /// <summary>当前条目（网格选中项 / 大图模式显示项）。</summary>
    public ScreenshotEntry? Current
    {
        get => _current;
        set
        {
            if (SetProperty(ref _current, value))
            {
                OnPropertyChanged(nameof(CurrentIndex));
                OnPropertyChanged(nameof(Count));
                OnPropertyChanged(nameof(CounterText));
                OnPropertyChanged(nameof(PositionText));
                NextImageCommand.RaiseCanExecuteChanged();
                PreviousImageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>是否处于大图查看模式（true=大图，false=网格）。</summary>
    public bool IsViewerMode
    {
        get => _isViewerMode;
        set => SetProperty(ref _isViewerMode, value);
    }

    public int CurrentIndex => IndexOfCurrent();

    public int Count => Items.Count;

    /// <summary>「第 N / M 张」计数文本。</summary>
    public string CounterText =>
        Count == 0 ? "无截图" : $"第 {CurrentIndex + 1} / {Count} 张";

    /// <summary>当前条目的位置时间码（HH:MM:SS.fff）。</summary>
    public string PositionText => Current is { } entry
        ? entry.Position.ToString(@"hh\:mm\:ss\.fff")
        : "--:--:--.---";

    // --- 命令 ---

    public RelayCommand EnterViewerCommand { get; }
    public RelayCommand ExitViewerCommand { get; }
    public RelayCommand NextImageCommand { get; }
    public RelayCommand PreviousImageCommand { get; }
    public RelayCommand OpenInExplorerCommand { get; }
    public RelayCommand AnnotateCommand { get; }
    public RelayCommand RemoveCommand { get; }

    /// <summary>标注请求：View 层打开标注编辑器。</summary>
    public event EventHandler<ScreenshotEntry>? AnnotateRequested;

    // --- 导航 ---

    /// <summary>当前项在 Items 中的下标；未选中/被过滤时返回 0（钳制）。</summary>
    private int IndexOfCurrent()
    {
        if (_current is null) return 0;
        for (int i = 0; i < Items.Count; i++)
        {
            if (ReferenceEquals(Items[i], _current))
                return i;
        }
        return 0;
    }

    /// <summary>当前项是否仍在 Items 中。</summary>
    private bool IsCurrentInItems()
    {
        if (_current is null) return false;
        for (int i = 0; i < Items.Count; i++)
        {
            if (ReferenceEquals(Items[i], _current))
                return true;
        }
        return false;
    }

    /// <summary>大图模式下一张（到末尾停止，不循环）。</summary>
    public void MoveNext()
    {
        var index = CurrentIndex;
        if (index < Count - 1)
            Current = Items[index + 1];
    }

    /// <summary>大图模式上一张（到开头停止，不循环）。</summary>
    public void MovePrevious()
    {
        var index = CurrentIndex;
        if (index > 0)
            Current = Items[index - 1];
    }

    /// <summary>按过滤结果刷新当前项（条目被删除/过滤后自动回退到相邻项）。</summary>
    private void RefreshCurrent()
    {
        OnPropertyChanged(nameof(Items));
        if (Items.Count == 0)
        {
            Current = null;
            IsViewerMode = false;
            return;
        }
        if (!IsCurrentInItems())
        {
            Current = Items[Math.Clamp(IndexOfCurrent(), 0, Items.Count - 1)];
        }
        if (_current is not null && !IsCurrentInItems())
            IsViewerMode = false;
    }
}
