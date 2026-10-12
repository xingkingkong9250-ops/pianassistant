using PianAssistant.Models;
using PianAssistant.ViewModels;
using Xunit;

namespace PianAssistant.Tests;

public class ClipListViewModelTests
{
    private const string Source = @"C:\v\video.mp4";

    [Fact]
    public void Add_SyncsQueue()
    {
        var vm = new ClipListViewModel();
        vm.Add(TimeSpan.Zero, TimeSpan.FromSeconds(1), Source, "#高光");
        Assert.Single(vm.Items);
        Assert.Single(vm.Queue);
        Assert.Equal("#高光", vm.Queue[0].Label);
        Assert.Equal("00:00:00 → 00:00:01", vm.Queue[0].RangeText);
        Assert.Equal("00:00:01", vm.Queue[0].DurationText);
        Assert.Equal("等待", vm.Queue[0].Status);
    }

    [Fact]
    public void Remove_RemovesFromItemsAndQueue()
    {
        var vm = new ClipListViewModel();
        var range = vm.Add(TimeSpan.Zero, TimeSpan.FromSeconds(1), Source);
        vm.Remove(range);
        Assert.Empty(vm.Items);
        Assert.Empty(vm.Queue);
    }

    [Fact]
    public void SearchText_FiltersByLabel()
    {
        var vm = new ClipListViewModel();
        vm.Add(TimeSpan.Zero, TimeSpan.FromSeconds(1), Source, "#高光 1");
        vm.Add(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), Source, "转场");
        vm.SearchText = "#高光";
        Assert.Single(vm.FilteredItems);
        Assert.Single(vm.FilteredQueue);
        Assert.Equal("#高光 1", vm.FilteredQueue[0].Label);
        vm.SearchText = "";
        Assert.Equal(2, vm.FilteredItems.Count);
    }

    [Fact]
    public void SearchText_ItemsWithoutLabel_FilteredOut()
    {
        var vm = new ClipListViewModel();
        vm.Add(TimeSpan.Zero, TimeSpan.FromSeconds(1), Source);
        vm.SearchText = "任意";
        Assert.Empty(vm.FilteredItems);
        Assert.Empty(vm.FilteredQueue);
    }

    [Fact]
    public async Task ExtractAllAsync_UpdatesStatusAndProgress()
    {
        var progressReported = new List<double>();
        var vm = new ClipListViewModel((range, re, ct, onProgress) =>
        {
            onProgress?.Invoke(0.5);
            return Task.FromResult(@"C:\o\clip.mp4");
        });
        vm.Add(TimeSpan.Zero, TimeSpan.FromSeconds(1), Source);
        vm.Add(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), Source);

        await vm.ExtractAllAsync();

        Assert.All(vm.Queue, q =>
        {
            Assert.Equal("完成", q.Status);
            Assert.Equal(1, q.Progress);
        });
        Assert.Equal(1, vm.BatchProgress);
        Assert.False(vm.IsBatchRunning);
    }

    [Fact]
    public async Task ExtractAllAsync_IncompleteItemsSkipped()
    {
        var vm = new ClipListViewModel((range, re, ct, onProgress) =>
            Task.FromResult(@"C:\o\clip.mp4"));
        vm.Add(new ClipRange(Source, TimeSpan.Zero)); // 不完整（无出点）
        vm.Add(TimeSpan.Zero, TimeSpan.FromSeconds(1), Source);

        await vm.ExtractAllAsync();

        Assert.Equal("等待", vm.Queue[0].Status);
        Assert.Equal("完成", vm.Queue[1].Status);
    }

    [Fact]
    public async Task ExtractAllAsync_ExtractFailure_MarksFailed()
    {
        var vm = new ClipListViewModel((range, re, ct, onProgress) =>
            Task.FromException<string>(new InvalidOperationException("boom")));
        vm.Add(TimeSpan.Zero, TimeSpan.FromSeconds(1), Source);

        await vm.ExtractAllAsync();

        Assert.Equal("失败", vm.Queue[0].Status);
        Assert.False(vm.IsBatchRunning);
    }

    [Fact]
    public async Task CancelBatchCommand_CancelsRunningBatch()
    {
        var started = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new ClipListViewModel(async (range, re, ct, onProgress) =>
        {
            started.TrySetResult(ct);
            await Task.Delay(Timeout.Infinite, ct); // 挂起直到取消
            return "unused";
        });
        vm.Add(TimeSpan.Zero, TimeSpan.FromSeconds(1), Source);
        vm.Add(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), Source);

        var run = vm.ExtractAllAsync();
        await started.Task;
        // 等第一项进入导出中状态
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!vm.Queue[0].Status.StartsWith("导出中") && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(vm.CancelBatchCommand.CanExecute(null));

        vm.CancelBatchCommand.Execute(null);
        await run;

        Assert.All(vm.Queue, q => Assert.Equal("已取消", q.Status));
        Assert.False(vm.IsBatchRunning);
    }

    [Fact]
    public void ConcatCandidates_OnlySameSourceComplete()
    {
        var vm = new ClipListViewModel();
        vm.Add(TimeSpan.Zero, TimeSpan.FromSeconds(1), Source);
        vm.Add(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), Source);
        vm.Add(TimeSpan.Zero, TimeSpan.FromSeconds(1), @"C:\v\other.mp4");
        vm.Add(new ClipRange(Source, TimeSpan.FromSeconds(2))); // 不完整

        var candidates = vm.ConcatCandidates;
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, c => Assert.Equal(Source, c.SourcePath));
    }

    [Fact]
    public void ConcatSourceName_UsesCandidateSource()
    {
        var vm = new ClipListViewModel();
        vm.Add(TimeSpan.Zero, TimeSpan.FromSeconds(1), @"C:\v\movie01.mp4");
        Assert.Equal("movie01", vm.ConcatSourceName);
    }

    [Fact]
    public async Task ConcatSelectedAsync_CallsConcatFn_WithCandidates()
    {
        IReadOnlyList<ClipRange>? received = null;
        string? receivedPath = null;
        var vm = new ClipListViewModel(
            concatFn: (ranges, path, ct, onProgress) =>
            {
                received = ranges;
                receivedPath = path;
                onProgress?.Invoke(0.5);
                return Task.FromResult(path);
            });
        vm.Add(TimeSpan.Zero, TimeSpan.FromSeconds(1), Source);
        vm.Add(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), Source);
        vm.Add(TimeSpan.Zero, TimeSpan.FromSeconds(1), @"C:\v\other.mp4");

        await vm.ConcatSelectedAsync(@"C:\o\joined.mp4");

        Assert.NotNull(received);
        Assert.Equal(2, received!.Count);
        Assert.Equal(@"C:\o\joined.mp4", receivedPath);
        Assert.Equal(1, vm.BatchProgress);
        Assert.False(vm.IsBatchRunning);
    }

    [Fact]
    public async Task ConcatSelectedAsync_DifferentSources_RaisesError()
    {
        var vm = new ClipListViewModel(
            concatFn: (ranges, path, ct, onProgress) =>
                Task.FromException<string>(new InvalidOperationException("不同源")));
        vm.Add(TimeSpan.Zero, TimeSpan.FromSeconds(1), Source);
        vm.Add(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), @"C:\v\other.mp4");

        string? error = null;
        vm.ConcatError += (_, msg) => error = msg;
        await vm.ConcatSelectedAsync(@"C:\o\joined.mp4");

        Assert.Contains("不同源", error);
        Assert.False(vm.IsBatchRunning);
    }

    [Fact]
    public async Task ConcatSelectedAsync_EmptyCandidates_DoesNothing()
    {
        var called = false;
        var vm = new ClipListViewModel(
            concatFn: (ranges, path, ct, onProgress) => { called = true; return Task.FromResult(path); });
        await vm.ConcatSelectedAsync(@"C:\o\joined.mp4");
        Assert.False(called);
    }
}
