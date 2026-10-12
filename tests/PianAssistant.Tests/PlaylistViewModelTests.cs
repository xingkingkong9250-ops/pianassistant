using PianAssistant.ViewModels;
using Xunit;

namespace PianAssistant.Tests;

public class PlaylistViewModelTests
{
    [Fact]
    public void AddPath_AddsToItems()
    {
        var vm = new PlaylistViewModel();
        var media = vm.AddPath("test.mp4");
        Assert.Single(vm.Items);
        Assert.Equal("test.mp4", media.Path);
    }

    [Fact]
    public void AddPath_FirstItem_BecomesCurrent()
    {
        var vm = new PlaylistViewModel();
        vm.AddPath("a.mp4");
        Assert.NotNull(vm.Current);
        Assert.Equal("a.mp4", vm.Current!.Path);
    }

    [Fact]
    public void Remove_Current_ResetsCurrentToFirst()
    {
        var vm = new PlaylistViewModel();
        var m1 = vm.AddPath("a.mp4");
        var m2 = vm.AddPath("b.mp4");
        vm.Select(m2);
        vm.Remove(m2);
        Assert.Equal("a.mp4", vm.Current?.Path);
    }

    [Fact]
    public void Clear_RemovesAllItems()
    {
        var vm = new PlaylistViewModel();
        vm.AddPath("a.mp4");
        vm.AddPath("b.mp4");
        vm.Clear();
        Assert.Empty(vm.Items);
        Assert.Null(vm.Current);
    }

    [Fact]
    public void MoveNext_WrapsAround()
    {
        var vm = new PlaylistViewModel();
        vm.AddPath("a.mp4");
        vm.AddPath("b.mp4");
        vm.AddPath("c.mp4");
        vm.MoveNext(); // a → b
        Assert.Equal("b.mp4", vm.Current?.Path);
        vm.MoveNext(); // b → c
        Assert.Equal("c.mp4", vm.Current?.Path);
        vm.MoveNext(); // c → a (wrap)
        Assert.Equal("a.mp4", vm.Current?.Path);
    }

    [Fact]
    public void MovePrevious_WrapsAround()
    {
        var vm = new PlaylistViewModel();
        vm.AddPath("a.mp4");
        vm.AddPath("b.mp4");
        vm.Select(vm.Items[0]); // current = a
        vm.MovePrevious(); // a → b (wrap to last)
        Assert.Equal("b.mp4", vm.Current?.Path);
    }

    [Fact]
    public void MoveNext_EmptyList_ReturnsFalse()
    {
        var vm = new PlaylistViewModel();
        Assert.False(vm.MoveNext());
    }
}
