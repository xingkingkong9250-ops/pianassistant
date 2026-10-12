using System.Collections.ObjectModel;
using System.Windows.Input;
using PianAssistant.Helpers;
using PianAssistant.Models;

namespace PianAssistant.ViewModels;

/// <summary>
/// Manages the play queue (playlist). Accepts drag-drop file paths
/// and single-click selection to switch the active media.
/// </summary>
public sealed class PlaylistViewModel : ObservableObject
{
    private MediaFile? _current;
    private int _currentIndex = -1;

    public ObservableCollection<MediaFile> Items { get; } = new();

    /// <summary>Currently active media (highlighted in the list, feeds the player).</summary>
    public MediaFile? Current
    {
        get => _current;
        set
        {
            if (SetProperty(ref _current, value))
            {
                CurrentIndex = value is null ? -1 : Items.IndexOf(value);
                SelectCurrent.RaiseCanExecuteChanged();
            }
        }
    }

    public int CurrentIndex
    {
        get => _currentIndex;
        private set => SetProperty(ref _currentIndex, value);
    }

    public RelayCommand SelectCurrent { get; }

    public event EventHandler<MediaFile?>? CurrentChanged;

    public PlaylistViewModel()
    {
        SelectCurrent = new RelayCommand(
            () => { if (_current is not null) CurrentChanged?.Invoke(this, _current); },
            () => _current is not null);
    }

    /// <summary>Add a media file to the end of the playlist.</summary>
    public void Add(MediaFile media)
    {
        ArgumentNullException.ThrowIfNull(media);
        Items.Add(media);
        if (_current is null)
            Current = media;
    }

    /// <summary>Convenience: create a MediaFile from a path and add it.</summary>
    public MediaFile AddPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var media = new MediaFile(path);
        Add(media);
        return media;
    }

    public void Remove(MediaFile media)
    {
        ArgumentNullException.ThrowIfNull(media);
        if (!Items.Remove(media)) return;
        if (ReferenceEquals(_current, media))
            Current = Items.Count > 0 ? Items[0] : null;
    }

    /// <summary>删除指定索引处的项目。如果删除的是当前项，自动切换到首项。</summary>
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= Items.Count) return;
        var media = Items[index];
        Remove(media);
    }

    /// <summary>
    /// 将项目从 oldIndex 移动到 newIndex，其余项目顺延。
    /// 重新计算当前项索引（Current 引用不变）。
    /// </summary>
    public void MoveItem(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Items.Count) return;
        if (newIndex < 0 || newIndex >= Items.Count) return;
        if (oldIndex == newIndex) return;
        var media = Items[oldIndex];
        Items.RemoveAt(oldIndex);
        Items.Insert(newIndex, media);
        // 修正当前项索引
        if (_current is not null)
            CurrentIndex = Items.IndexOf(_current);
    }

    /// <summary>获取指定索引处项目的路径。越界返回 null。</summary>
    public string? GetPath(int index)
    {
        if (index < 0 || index >= Items.Count) return null;
        return Items[index].Path;
    }

    public void Clear()
    {
        Items.Clear();
        Current = null;
    }

    /// <summary>
    /// Programmatically select an item (used by double-click in the list).
    /// Raises <see cref="CurrentChanged"/> so the Shell can swap the media.
    /// </summary>
    public void Select(MediaFile media)
    {
        ArgumentNullException.ThrowIfNull(media);
        if (!Items.Contains(media)) return;
        Current = media;
        CurrentChanged?.Invoke(this, media);
    }

    /// <summary>Move to the next item; wraps to the first. Returns false if empty.</summary>
    public bool MoveNext()
    {
        if (Items.Count == 0) return false;
        var idx = _currentIndex < 0 ? 0 : (_currentIndex + 1) % Items.Count;
        Current = Items[idx];
        CurrentChanged?.Invoke(this, _current);
        return true;
    }

    /// <summary>Move to the previous item; wraps to the last. Returns false if empty.</summary>
    public bool MovePrevious()
    {
        if (Items.Count == 0) return false;
        var idx = _currentIndex <= 0 ? Items.Count - 1 : _currentIndex - 1;
        Current = Items[idx];
        CurrentChanged?.Invoke(this, _current);
        return true;
    }
}
