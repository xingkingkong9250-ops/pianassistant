using System.Collections.ObjectModel;
using System.Windows.Input;
using PianAssistant.Helpers;
using PianAssistant.Models;

namespace PianAssistant.ViewModels;

/// <summary>
/// List of clip ranges (in/out markers) collected from the timeline.
/// Supports batch extraction with progress and per-item re-encode option.
/// </summary>
public sealed class ClipListViewModel : ObservableObject
{
    private ClipRange? _selected;
    private double _batchProgress;
    private bool _isBatchRunning;

    public ObservableCollection<ClipRange> Items { get; } = new();

    public ClipRange? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
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
        private set => SetProperty(ref _isBatchRunning, value);
    }

    public ICommand ExtractAllCommand { get; }

    private readonly Func<ClipRange, bool, CancellationToken, Action<double>?, Task<string>>? _extractFn;

    public ClipListViewModel(
        Func<ClipRange, bool, CancellationToken, Action<double>?, Task<string>>? extractFn = null)
    {
        _extractFn = extractFn;
        ExtractAllCommand = new AsyncRelayCommand(
            ExtractAllAsync,
            () => !IsBatchRunning && Items.Count > 0 && _extractFn is not null);
    }

    /// <summary>Add a new clip range (typically from PlayerVM MarkIn/MarkOut).</summary>
    public void Add(ClipRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        Items.Add(range);
    }

    /// <summary>Create and add a clip from in/out points.</summary>
    public ClipRange Add(TimeSpan inPoint, TimeSpan outPoint, string sourcePath, string? label = null)
    {
        if (outPoint <= inPoint)
            throw new ArgumentException("outPoint must be after inPoint.", nameof(outPoint));
        var range = new ClipRange(sourcePath, inPoint, outPoint, label);
        Items.Add(range);
        return range;
    }

    public void Remove(ClipRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        Items.Remove(range);
    }

    public void Clear()
    {
        Items.Clear();
        Selected = null;
        BatchProgress = 0;
    }

    /// <summary>Batch-extract all complete clips via the injected extract function.</summary>
    public async Task ExtractAllAsync()
    {
        if (_extractFn is null || IsBatchRunning) return;

        IsBatchRunning = true;
        BatchProgress = 0;
        try
        {
            var complete = Items.Where(c => c.IsComplete).ToList();
            for (int i = 0; i < complete.Count; i++)
            {
                var ratio = (double)i / complete.Count;
                BatchProgress = ratio;
                await _extractFn(complete[i], false, CancellationToken.None, p =>
                {
                    var per = 1.0 / complete.Count;
                    BatchProgress = ratio + p * per;
                });
            }
            BatchProgress = 1;
        }
        finally
        {
            IsBatchRunning = false;
        }
    }
}
