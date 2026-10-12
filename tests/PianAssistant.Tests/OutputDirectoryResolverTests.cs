using System.IO;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>输出目录组织规则（基目录 + yyyyMMdd + 项目名）纯逻辑测试。</summary>
public class OutputDirectoryResolverTests
{
    [Fact]
    public void Resolve_NoOptions_ReturnsBaseDirectory()
    {
        var result = OutputDirectoryResolver.Resolve(
            @"C:\out\shots", organizeByDate: false, projectSubfolder: null,
            new DateTime(2026, 10, 12));
        Assert.Equal(@"C:\out\shots", result);
    }

    [Fact]
    public void Resolve_ByDate_AddsDateSubfolder()
    {
        var result = OutputDirectoryResolver.Resolve(
            @"C:\out\shots", organizeByDate: true, projectSubfolder: null,
            new DateTime(2026, 10, 12));
        Assert.Equal(Path.Combine(@"C:\out\shots", "20261012"), result);
    }

    [Fact]
    public void Resolve_ProjectName_AddsSubfolder()
    {
        var result = OutputDirectoryResolver.Resolve(
            @"C:\out\clips", organizeByDate: false, projectSubfolder: "钢琴课01",
            new DateTime(2026, 10, 12));
        Assert.Equal(Path.Combine(@"C:\out\clips", "钢琴课01"), result);
    }

    [Fact]
    public void Resolve_BothOptions_DateThenProject()
    {
        var result = OutputDirectoryResolver.Resolve(
            @"C:\out\shots", organizeByDate: true, projectSubfolder: "项目A",
            new DateTime(2026, 10, 12));
        Assert.Equal(Path.Combine(@"C:\out\shots", "20261012", "项目A"), result);
    }

    [Fact]
    public void Resolve_BlankProjectName_TreatedAsDisabled()
    {
        var result = OutputDirectoryResolver.Resolve(
            @"C:\out\shots", organizeByDate: false, projectSubfolder: "   ",
            new DateTime(2026, 10, 12));
        Assert.Equal(@"C:\out\shots", result);
    }

    [Fact]
    public void Sanitize_ReplacesInvalidChars()
    {
        Assert.Equal("a_b_c", OutputDirectoryResolver.Sanitize("a/b\\c"));
        Assert.Equal("项目_名称", OutputDirectoryResolver.Sanitize("项目:名称"));
    }

    [Fact]
    public void Sanitize_BlankOrEmpty_ReturnsNull()
    {
        Assert.Null(OutputDirectoryResolver.Sanitize(null));
        Assert.Null(OutputDirectoryResolver.Sanitize(""));
        Assert.Null(OutputDirectoryResolver.Sanitize("  "));
    }

    [Fact]
    public void Sanitize_TrimsWhitespaceAndTrailingDots()
    {
        Assert.Equal("abc", OutputDirectoryResolver.Sanitize("  abc  "));
        Assert.Equal("abc", OutputDirectoryResolver.Sanitize("abc."));
    }
}
