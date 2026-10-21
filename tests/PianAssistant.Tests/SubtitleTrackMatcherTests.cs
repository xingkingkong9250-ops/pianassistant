using System.IO;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// <see cref="SubtitleTrackMatcher"/> 纯函数匹配规则与 VM 轨到切换的单测。
/// </summary>
public class SubtitleTrackMatcherTests
{
    // --- MatchSubtitleNames ---

    [Fact]
    public void Match_ExactSameStem_ReturnsMatch()
    {
        var result = SubtitleTrackMatcher.MatchSubtitleNames("movie.mp4", new[] { "movie.srt" });
        Assert.Single(result);
        Assert.Equal("movie.srt", result[0]);
    }

    [Fact]
    public void Match_LanguageSuffixMatched()
    {
        var result = SubtitleTrackMatcher.MatchSubtitleNames(
            "movie.mp4", new[] { "movie.zh.srt", "movie.en.ass" });
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Match_ExtensionIsCaseInsensitive()
    {
        var result = SubtitleTrackMatcher.MatchSubtitleNames("movie.mp4", new[] { "movie.SRT" });
        Assert.Single(result);
    }

    [Fact]
    public void Match_VideoFileItselfNotMatched()
    {
        var result = SubtitleTrackMatcher.MatchSubtitleNames(
            "movie.mp4", new[] { "movie.mp4", "movie.avi", "movie.mkv" });
        Assert.Empty(result);
    }

    [Fact]
    public void Match_DifferentStemNotMatched()
    {
        var result = SubtitleTrackMatcher.MatchSubtitleNames(
            "movie.mp4", new[] { "other.srt", "movie2.srt", "mov.srt" });
        Assert.Empty(result);
    }

    [Fact]
    public void Match_PrefixRequiresDotBoundary()
    {
        // "movies.srt" 主名 "movies" 以 "movie" 开头但缺少「.」分隔，不匹配
        var result = SubtitleTrackMatcher.MatchSubtitleNames("movie.mp4", new[] { "movies.srt" });
        Assert.Empty(result);
    }

    [Fact]
    public void Match_UnsupportedExtensionNotMatched()
    {
        var result = SubtitleTrackMatcher.MatchSubtitleNames(
            "movie.mp4", new[] { "movie.txt", "movie.idx", "movie.png" });
        Assert.Empty(result);
    }

    [Fact]
    public void Match_ResultsSortedByName()
    {
        var result = SubtitleTrackMatcher.MatchSubtitleNames(
            "movie.mp4", new[] { "movie.zh.srt", "movie.ass", "movie.en.srt" });
        Assert.Equal(new[] { "movie.ass", "movie.en.srt", "movie.zh.srt" }, result);
    }

    [Fact]
    public void Match_StemCaseInsensitive()
    {
        var result = SubtitleTrackMatcher.MatchSubtitleNames("Movie.MP4", new[] { "MOVIE.srt" });
        Assert.Single(result);
    }

    [Fact]
    public void Match_EmptyCandidatesReturnsEmpty()
    {
        Assert.Empty(SubtitleTrackMatcher.MatchSubtitleNames("movie.mp4", Array.Empty<string>()));
    }

    // --- IsSubtitleFile ---

    [Theory]
    [InlineData("a.srt", true)]
    [InlineData("a.ass", true)]
    [InlineData("a.ssa", true)]
    [InlineData("a.vtt", true)]
    [InlineData("a.sub", true)]
    [InlineData("a.SRT", true)]
    [InlineData("a.mp4", false)]
    [InlineData("a.txt", false)]
    [InlineData("noext", false)]
    public void IsSubtitleFile_ClassifiesByExtension(string path, bool expected)
        => Assert.Equal(expected, SubtitleTrackMatcher.IsSubtitleFile(path));

    // --- FindMatchingSubtitles（磁盘集成）---

    [Fact]
    public void Find_OnDisk_MatchesAndReturnsFullPaths()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pian-sub-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var video = Path.Combine(dir, "movie.mp4");
            File.WriteAllText(video, "stub");
            File.WriteAllText(Path.Combine(dir, "movie.srt"), "stub");
            File.WriteAllText(Path.Combine(dir, "movie.zh.ass"), "stub");
            File.WriteAllText(Path.Combine(dir, "other.srt"), "stub");

            var result = SubtitleTrackMatcher.FindMatchingSubtitles(video);
            Assert.Equal(2, result.Count);
            Assert.All(result, p => Assert.True(Path.IsPathFullyQualified(p)));
            Assert.Contains(Path.Combine(dir, "movie.srt"), result);
            Assert.Contains(Path.Combine(dir, "movie.zh.ass"), result);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Find_MissingDirectoryReturnsEmpty()
    {
        var bogus = Path.Combine(Path.GetTempPath(), $"pian-sub-none-{Guid.NewGuid():N}", "movie.mp4");
        Assert.Empty(SubtitleTrackMatcher.FindMatchingSubtitles(bogus));
    }
}
