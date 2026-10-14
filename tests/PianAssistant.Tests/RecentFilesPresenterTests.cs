using PianAssistant.ViewModels;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// 最近文件菜单项生成测试（M6 C1）：空列表占位禁用、文件名 + 完整路径 ToolTip、数量上限。
/// </summary>
public class RecentFilesPresenterTests
{
    [Fact]
    public void Build_Empty_ReturnsDisabledPlaceholder()
    {
        var items = RecentFilesPresenter.Build(new List<string>());
        var only = Assert.Single(items);
        Assert.Equal("（无最近文件）", only.Header);
        Assert.Null(only.Path); // Path 为 null 表示禁用占位项
    }

    [Fact]
    public void Build_Null_ReturnsDisabledPlaceholder()
    {
        var items = RecentFilesPresenter.Build(null);
        Assert.Single(items);
        Assert.Null(items[0].Path);
    }

    [Fact]
    public void Build_ListsFileNameWithFullPathTooltip()
    {
        var path = @"C:\media\video.mp4";
        var items = RecentFilesPresenter.Build(new List<string> { path });
        var only = Assert.Single(items);
        Assert.Equal("video.mp4", only.Header);
        Assert.Equal(path, only.ToolTip);
        Assert.Equal(path, only.Path);
    }

    [Fact]
    public void Build_CapsAtMaxItems()
    {
        var files = Enumerable.Range(0, 20).Select(i => $@"C:\m\f{i}.mp4").ToList();
        var items = RecentFilesPresenter.Build(files);
        Assert.Equal(RecentFilesPresenter.MaxItems, items.Count);
    }

    [Fact]
    public void Build_SkipsBlankPaths_AndFallsBackToPlaceholder()
    {
        var items = RecentFilesPresenter.Build(new List<string> { "", "   " });
        var only = Assert.Single(items);
        Assert.Null(only.Path);
    }
}
