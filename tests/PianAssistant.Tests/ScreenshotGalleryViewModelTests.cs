using PianAssistant.Models;
using PianAssistant.ViewModels;
using Xunit;

namespace PianAssistant.Tests;

public class ScreenshotGalleryViewModelTests
{
    private static ScreenshotEntry MakeEntry(
        string outputPath, TimeSpan position, DateTime capturedAt, string? label = null) =>
        new(@"C:\v\video.mp4", outputPath, position, capturedAt, label);

    [Fact]
    public void Add_AddsToItems_AndSelects()
    {
        var vm = new ScreenshotGalleryViewModel();
        var entry = MakeEntry(@"C:\o\s1.png", TimeSpan.FromSeconds(1), DateTime.Today);
        vm.Add(entry);
        Assert.Single(vm.Items);
        Assert.Equal(entry, vm.Selected);
    }

    [Fact]
    public void SearchText_Empty_ReturnsAll()
    {
        var vm = new ScreenshotGalleryViewModel();
        vm.Add(MakeEntry(@"C:\o\s1.png", TimeSpan.FromSeconds(1), DateTime.Today, "#构图 车站"));
        vm.Add(MakeEntry(@"C:\o\s2.png", TimeSpan.FromSeconds(2), DateTime.Today, "空镜"));
        Assert.Equal(2, vm.FilteredItems.Count);
    }

    [Fact]
    public void SearchText_PlainText_FiltersByLabelContains()
    {
        var vm = new ScreenshotGalleryViewModel();
        vm.Add(MakeEntry(@"C:\o\s1.png", TimeSpan.FromSeconds(1), DateTime.Today, "#构图 车站"));
        vm.Add(MakeEntry(@"C:\o\s2.png", TimeSpan.FromSeconds(2), DateTime.Today, "空镜"));
        vm.SearchText = "车站";
        Assert.Single(vm.FilteredItems);
        Assert.Equal(@"C:\o\s1.png", vm.FilteredItems[0].OutputPath);
    }

    [Fact]
    public void SearchText_TagStyle_MatchesHashTag()
    {
        var vm = new ScreenshotGalleryViewModel();
        vm.Add(MakeEntry(@"C:\o\s1.png", TimeSpan.FromSeconds(1), DateTime.Today, "#构图 对称"));
        vm.Add(MakeEntry(@"C:\o\s2.png", TimeSpan.FromSeconds(2), DateTime.Today, "#色调 冷暖对比"));
        vm.SearchText = "#构图";
        Assert.Single(vm.FilteredItems);
        Assert.Equal(@"C:\o\s1.png", vm.FilteredItems[0].OutputPath);
    }

    [Fact]
    public void SearchText_CaseInsensitive()
    {
        var vm = new ScreenshotGalleryViewModel();
        vm.Add(MakeEntry(@"C:\o\s1.png", TimeSpan.FromSeconds(1), DateTime.Today, "CloseUp"));
        vm.SearchText = "closeup";
        Assert.Single(vm.FilteredItems);
    }

    [Fact]
    public void SearchText_ItemsWithoutLabel_AreFilteredOut()
    {
        var vm = new ScreenshotGalleryViewModel();
        vm.Add(MakeEntry(@"C:\o\s1.png", TimeSpan.FromSeconds(1), DateTime.Today));
        vm.SearchText = "任意";
        Assert.Empty(vm.FilteredItems);
    }

    [Fact]
    public void FilterDateRange_FiltersByCapturedAtDate()
    {
        var vm = new ScreenshotGalleryViewModel();
        vm.Add(MakeEntry(@"C:\o\s1.png", TimeSpan.FromSeconds(1), new DateTime(2026, 10, 10, 8, 0, 0)));
        vm.Add(MakeEntry(@"C:\o\s2.png", TimeSpan.FromSeconds(2), new DateTime(2026, 10, 12, 20, 0, 0)));
        vm.FilterStart = new DateTime(2026, 10, 12);
        Assert.Single(vm.FilteredItems);
        Assert.Equal(@"C:\o\s2.png", vm.FilteredItems[0].OutputPath);
        vm.FilterEnd = new DateTime(2026, 10, 12);
        Assert.Single(vm.FilteredItems);
        vm.FilterStart = null;
        vm.FilterEnd = new DateTime(2026, 10, 10);
        Assert.Equal(@"C:\o\s1.png", vm.FilteredItems[0].OutputPath);
    }

    [Fact]
    public void SearchText_CombinedWithDateRange()
    {
        var vm = new ScreenshotGalleryViewModel();
        vm.Add(MakeEntry(@"C:\o\s1.png", TimeSpan.FromSeconds(1), new DateTime(2026, 10, 10), "#构图 A"));
        vm.Add(MakeEntry(@"C:\o\s2.png", TimeSpan.FromSeconds(2), new DateTime(2026, 10, 12), "#构图 B"));
        vm.SearchText = "#构图";
        Assert.Equal(2, vm.FilteredItems.Count);
        vm.FilterStart = new DateTime(2026, 10, 12);
        Assert.Single(vm.FilteredItems);
        Assert.Equal(@"C:\o\s2.png", vm.FilteredItems[0].OutputPath);
    }

    [Fact]
    public void SearchText_RefreshesWhenItemsAdded()
    {
        var vm = new ScreenshotGalleryViewModel();
        vm.SearchText = "#构图";
        Assert.Empty(vm.FilteredItems);
        vm.Add(MakeEntry(@"C:\o\s1.png", TimeSpan.FromSeconds(1), DateTime.Today, "#构图 对称"));
        Assert.Single(vm.FilteredItems);
    }

    [Fact]
    public void AddAnnotatedCopy_PositionSame_LabelSuffix()
    {
        var vm = new ScreenshotGalleryViewModel();
        var original = MakeEntry(@"C:\o\s1.png", TimeSpan.FromSeconds(42.5),
            new DateTime(2026, 10, 12), "#构图");
        vm.Add(original);

        var annotated = vm.AddAnnotatedCopy(original, @"C:\o\s1_annotated.png");

        Assert.Equal(original.Position, annotated.Position);
        Assert.Equal("#构图(标注)", annotated.Label);
        Assert.Equal(original.SourcePath, annotated.SourcePath);
        Assert.Equal(original.CapturedAt, annotated.CapturedAt);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(annotated, vm.Selected);
        // 原图不被修改
        Assert.Equal("#构图", original.Label);
    }

    [Fact]
    public void AddAnnotatedCopy_NullLabel_GetsSuffixOnly()
    {
        var vm = new ScreenshotGalleryViewModel();
        var original = MakeEntry(@"C:\o\s1.png", TimeSpan.FromSeconds(1), DateTime.Today);
        vm.Add(original);
        var annotated = vm.AddAnnotatedCopy(original, @"C:\o\s1_annotated.png");
        Assert.Equal("(标注)", annotated.Label);
    }

    [Fact]
    public void Remove_RemovesFromItems()
    {
        var vm = new ScreenshotGalleryViewModel();
        var entry = MakeEntry(@"C:\o\s1.png", TimeSpan.FromSeconds(1), DateTime.Today);
        vm.Add(entry);
        vm.Remove(entry);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        var vm = new ScreenshotGalleryViewModel();
        vm.Add(MakeEntry(@"C:\o\s1.png", TimeSpan.FromSeconds(1), DateTime.Today));
        vm.Add(MakeEntry(@"C:\o\s2.png", TimeSpan.FromSeconds(2), DateTime.Today));
        vm.Clear();
        Assert.Empty(vm.Items);
        Assert.Null(vm.Selected);
    }
}
