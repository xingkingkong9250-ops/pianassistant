using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using PianAssistant.Helpers;
using PianAssistant.Models;

namespace PianAssistant.ViewModels;

/// <summary>
/// List of clip ranges (in/out markers) collected from the timeline.
/// Supports batch extraction with progress, cancellation and per-item
/// status, multi-select concatenation, and Label-based filtering.
/// </summary>
public sealed class ClipListViewModel : ObservableObject
{
    private ClipRange? _selected;
    private ClipQueueItem? _selectedQueueItem;
    private double _batchProgress;
    private bool _isBatchRunning;
    private string _searchText = string.Empty;
    private IReadOnlyList<ClipRange> _filteredItems = Array.Empty<ClipRange>();
    private IReadOnlyList<ClipQueueItem> _filteredQueue = Array.Empty<ClipQueueItem>();
    private CancellationTokenSource? _batchCts;

    public ObservableCollection<ClipRange> Items { get; } = new();

    /// <summary>批量导出队列（与 Items 同步增删；状态跟踪见 <see cref="ClipQueueItem"/>）。</summary>
    public ObservableCollection<ClipQueueItem> Queue { get; } = new();

    /// <summary>按搜索文本过滤后的片段（供 UI 绑定与拼接候选）。</summary>
    public IReadOnlyList<ClipRange> FilteredItems => _filteredItems;

    /// <summary>按搜索文本过滤后的队列项（供批量面板绑定）。</summary>
    public IReadOnlyList<ClipQueueItem> FilteredQueue => _filteredQueue;

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

    public ClipRange? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    /// <summary>批量面板当前选中的队列项；同时同步 <see cref="Selected"/>。</summary>
    public ClipQueueItem? SelectedQueueItem
    {
        get => _selectedQueueItem;
        set
        {
            if (SetProperty(ref _selectedQueueItem, value))
                Selected = value?.Range;
        }
    }

    /// <summary>0..1 during batch extraction; 0 when idle.</summary>
    public double BatchProgress
    {
        get => _batchProgress;
        private set => SetProperty(ref _batchProgress, value);
    }

    public bool IsBatchRunning
    {
        get => _isBatchRunning;
        private set
        {
            if (SetProperty(ref _isBatchRunning, value))
            {
                ExtractAllCommand.RaiseCanExecuteChanged();
                ConcatSelectedCommand.RaiseCanExecuteChanged();
                CancelBatchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand ExtractAllCommand { get; }
    /// <summary>取消当前批量任务（触发 CancellationTokenSource.Cancel）。</summary>
    public RelayCommand CancelBatchCommand { get; }
    /// <summary>拼接导出请求：View 层弹 SaveFileDialog 后调用 ConcatSelectedAsync。</summary>
    public RelayCommand ConcatSelectedCommand { get; }

    /// <summary>拼接候选：与选中项（或第一个完整项）同源的完整片段。</summary>
    public IReadOnlyList<ClipRange> ConcatCandidates
    {
        get
        {
            var complete = Items.Where(c => c.IsComplete).ToList();
            if (complete.Count == 0) return complete;
            var source = Selected?.SourcePath ?? complete[0].SourcePath;
            return complete
                .Where(c => string.Equals(c.SourcePath, source, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    /// <summary>拼接候选的源视频名（不含扩展名），用于默认输出文件名。</summary>
    public string ConcatSourceName
    {
        get
        {
            var candidates = ConcatCandidates;
            return candidates.Count == 0
                ? "片段拼接"
                : Path.GetFileNameWithoutExtension(candidates[0].SourcePath);
        }
    }

    /// <summary>拼接请求：View 层弹出保存对话框。</summary>
    public event EventHandler? ConcatRequested;

    /// <summary>拼接失败（含不同源等校验错误）：View 层弹提示。</summary>
    public event EventHandler<string>? ConcatError;

    private readonly Func<ClipRange, bool, CancellationToken, Action<double>?, Task<string>>? _extractFn;
    private readonly Func<IReadOnlyList<ClipRange>, string, CancellationToken, Action<double>?, Task<string>>? _concatFn;

    public ClipListViewModel(
        Func<ClipRange, bool, CancellationToken, Action<double>?, Task<string>>? extractFn = null,
        Func<IReadOnlyList<ClipRange>, string, CancellationToken, Action<double>?, Task<string>>? concatFn = null)
    {
        _extractFn = extractFn;
        _concatFn = concatFn;
        // 集合任何变化都同步刷新过滤结果
        Items.CollectionChanged += (_, _) => RefreshFilter();
        RefreshFilter();
        ExtractAllCommand = new AsyncRelayCommand(
            ExtractAllAsync,
            () => !IsBatchRunning && Items.Count > 0 && _extractFn is not null);
        CancelBatchCommand = new RelayCommand(
            () => _batchCts?.Cancel(),
            () => IsBatchRunning);
        ConcatSelectedCommand = new RelayCommand(
            () => ConcatRequested?.Invoke(this, EventArgs.Empty),
            () => !IsBatchRunning && _concatFn is not null && ConcatCandidates.Count > 0);
    }

    /// <summary>Add a new clip range (typically from PlayerVM MarkIn/MarkOut).</summary>
    public void Add(ClipRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        Items.Add(range);
        Queue.Add(new ClipQueueItem(range));
    }

    /// <summary>Create and add a clip from in/out points.</summary>
    public ClipRange Add(TimeSpan inPoint, TimeSpan outPoint, string sourcePath, string? label = null)
    {
        if (outPoint <= inPoint)
            throw new ArgumentException("outPoint must be after inPoint.", nameof(outPoint));
        var range = new ClipRange(sourcePath, inPoint, outPoint, label);
        Add(range);
        return range;
    }

    public void Remove(ClipRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        Items.Remove(range);
        var queueItem = Queue.FirstOrDefault(q => ReferenceEquals(q.Range, range));
        if (queueItem is not null) Queue.Remove(queueItem);
    }

    public void Clear()
    {
        Items.Clear();
        Queue.Clear();
        Selected = null;
        SelectedQueueItem = null;
        BatchProgress = 0;
    }

    /// <summary>
    /// Batch-extract all complete clips via the injected extract function.
    /// 每完成一项刷新该项状态文本；「取消」会终止后续项并全部标记已取消。
    /// </summary>
    public async Task ExtractAllAsync()
    {
        if (_extractFn is null || IsBatchRunning) return;
        var targets = Queue.Where(q => q.Range.IsComplete).ToList();
        if (targets.Count == 0) return;

        IsBatchRunning = true;
        BatchProgress = 0;
        _batchCts = new CancellationTokenSource();
        var cancelled = false;
        try
        {
            var token = _batchCts.Token;
            for (int i = 0; i < targets.Count; i++)
            {
                if (cancelled) break;
                var item = targets[i];
                var ratio = (double)i / targets.Count;
                var per = 1.0 / targets.Count;
                BatchProgress = ratio;
                item.Status = "导出中 0%";
                try
                {
                    await _extractFn(item.Range, false, token, p =>
                    {
                        item.Progress = p;
                        item.Status = $"导出中 {p * 100:0}%";
                        BatchProgress = ratio + p * per;
                    });
                    item.Status = "完成";
                    item.Progress = 1;
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    item.Status = "已取消";
                }
                catch (Exception)
                {
                    item.Status = "失败";
                }
            }
            if (cancelled)
            {
                // 剩余未处理项标记为已取消
                foreach (var item in targets.Where(t => t.Status == "等待"))
                    item.Status = "已取消";
            }
            else
            {
                BatchProgress = 1;
            }
        }
        finally
        {
            IsBatchRunning = false;
            _batchCts.Dispose();
            _batchCts = null;
        }
    }

    /// <summary>
    /// 拼接导出候选片段到指定路径（由 View 层经 SaveFileDialog 选定）。
    /// 成功后 BatchProgress=1；不同源/失败经 <see cref="ConcatError"/> 上报。
    /// </summary>
    public async Task ConcatSelectedAsync(string outputPath)
    {
        if (_concatFn is null || IsBatchRunning) return;
        var candidates = ConcatCandidates;
        if (candidates.Count == 0) return;

        IsBatchRunning = true;
        BatchProgress = 0;
        _batchCts = new CancellationTokenSource();
        try
        {
            await _concatFn(candidates, outputPath, _batchCts.Token, p => BatchProgress = p);
            BatchProgress = 1;
        }
        catch (OperationCanceledException)
        {
            // 用户取消：静默恢复空闲
        }
        catch (Exception ex)
        {
            ConcatError?.Invoke(this, ex.Message);
        }
        finally
        {
            IsBatchRunning = false;
            _batchCts.Dispose();
            _batchCts = null;
        }
    }

    /// <summary>按搜索文本（Label 子串）重建过滤结果。</summary>
    private void RefreshFilter()
    {
        var text = _searchText?.Trim();
        IEnumerable<ClipRange> query = Items;
        IEnumerable<ClipQueueItem> queryQueue = Queue;
        if (!string.IsNullOrEmpty(text))
        {
            query = query.Where(c =>
                c.Label is not null && c.Label.Contains(text, StringComparison.OrdinalIgnoreCase));
            queryQueue = queryQueue.Where(q =>
                q.Label is not null && q.Label.Contains(text, StringComparison.OrdinalIgnoreCase));
        }
        _filteredItems = query.ToList();
        _filteredQueue = queryQueue.ToList();
        OnPropertyChanged(nameof(FilteredItems));
        OnPropertyChanged(nameof(FilteredQueue));
    }
}
