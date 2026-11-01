using System.IO;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// v1.15.6：片段导出保留字幕的核心逻辑——SRT 时间轴按片段起止平移/裁剪。
/// </summary>
public class SubtitleTimelineTests : IDisposable
{
    private readonly string _tempDir;

    public SubtitleTimelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-sub-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string WriteSource(string content)
    {
        var path = Path.Combine(_tempDir, $"src{Guid.NewGuid():N}.srt");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Shift_TranslatesTimecodesAndClipsToDuration()
    {
        var source = WriteSource(
            "1\r\n00:00:01,000 --> 00:00:03,000\r\n第一句\r\n\r\n" +
            "2\r\n00:00:04,000 --> 00:00:08,000\r\n第二句\r\n\r\n" +
            "3\r\n00:00:08,000 --> 00:00:09,000\r\n第三句\r\n");
        var target = Path.Combine(_tempDir, "out.srt");

        var written = SubtitleTimeline.Shift(
            source, target, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(8));

        Assert.Equal(3, written);
        var text = File.ReadAllText(target);
        // 1.0-3.0 → 0.5-2.5；4.0-8.0 → 3.5-7.5；8.0-9.0 尾部裁到片段结束 8.0
        Assert.Contains("00:00:00,500 --> 00:00:02,500", text);
        Assert.Contains("00:00:03,500 --> 00:00:07,500", text);
        Assert.Contains("00:00:07,500 --> 00:00:08,000", text);
        Assert.Contains("第一句", text);
        Assert.Contains("第三句", text);
    }

    [Fact]
    public void Shift_CueStraddlingInPoint_StartsAtZero()
    {
        var source = WriteSource(
            "1\r\n00:00:10,000 --> 00:00:15,000\r\n跨入点\r\n");
        var target = Path.Combine(_tempDir, "out.srt");

        var written = SubtitleTimeline.Shift(
            source, target, TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(5));

        Assert.Equal(1, written);
        Assert.Contains("00:00:00,000 --> 00:00:03,000", File.ReadAllText(target));
    }

    [Fact]
    public void Shift_CuesOutsideRange_ReturnsZero()
    {
        var source = WriteSource(
            "1\r\n00:00:01,000 --> 00:00:02,000\r\n片段之前\r\n\r\n" +
            "2\r\n00:00:20,000 --> 00:00:21,000\r\n片段之后\r\n");
        var target = Path.Combine(_tempDir, "out.srt");

        var written = SubtitleTimeline.Shift(
            source, target, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));

        Assert.Equal(0, written);
        Assert.Empty(File.ReadAllText(target).Trim());
    }

    [Fact]
    public void Shift_NonPositiveDuration_ReturnsZero()
    {
        var source = WriteSource("1\r\n00:00:01,000 --> 00:00:02,000\r\n文本\r\n");
        var target = Path.Combine(_tempDir, "out.srt");

        Assert.Equal(0, SubtitleTimeline.Shift(
            source, target, TimeSpan.Zero, TimeSpan.Zero));
    }

    [Fact]
    public void Shift_MultiLineTextAndSequentialNumbering()
    {
        var source = WriteSource(
            "1\r\n00:00:02,000 --> 00:00:04,000\r\n上行\r\n下行\r\n\r\n" +
            "7\r\n00:00:06,000 --> 00:00:07,000\r\n后面\r\n");
        var target = Path.Combine(_tempDir, "out.srt");

        var written = SubtitleTimeline.Shift(
            source, target, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));

        Assert.Equal(2, written);
        var lines = File.ReadAllLines(target);
        // 重新编号为 1、2，而不是沿用源编号 1、7
        Assert.Equal("1", lines[0]);
        Assert.Contains("00:00:01,000 --> 00:00:03,000", lines[1]);
        Assert.Equal("上行", lines[2]);
        Assert.Equal("下行", lines[3]);
        Assert.Contains("00:00:05,000 --> 00:00:06,000", File.ReadAllText(target));
    }
}
