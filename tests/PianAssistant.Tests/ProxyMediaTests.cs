using System.IO;
using PianAssistant.Models;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>代理媒体（v1.11.0）：探测判定、档位解析、缓存路径与命中规则。</summary>
public class ProxyMediaTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppSettingsService _settings;
    private readonly FfmpegRunner _ffmpeg;
    private readonly ProxyMediaService _proxies;

    public ProxyMediaTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-proxy-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var ffmpegPath = Path.Combine(_tempDir, "ffmpeg.exe");
        var ffprobePath = Path.Combine(_tempDir, "ffprobe.exe");
        File.WriteAllText(ffmpegPath, "stub");
        File.WriteAllText(ffprobePath, "stub");
        _ffmpeg = new FfmpegRunner(ffmpegPath, ffprobePath);
        _settings = new AppSettingsService(Path.Combine(_tempDir, "settings"));
        _proxies = new ProxyMediaService(_ffmpeg, _settings, Path.Combine(_tempDir, "proxy"));
    }

    public void Dispose()
    {
        _proxies.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // --- 分辨率判定 ---

    [Theory]
    [InlineData(1920, 1080, false)]
    [InlineData(1280, 720, false)]
    [InlineData(4096, 2160, true)]
    [InlineData(3840, 2160, true)]
    [InlineData(2160, 3840, true)] // 竖屏 4K
    [InlineData(1920, 1200, true)] // 短边超 1080
    [InlineData(2048, 1080, true)] // 长边超 1920（DCI 2K）
    public void IsAbove1080P_JudgesByEdges(int width, int height, bool expected)
    {
        var probe = new MediaProbeResult(width, height, TimeSpan.FromHours(1), 0);
        Assert.Equal(expected, probe.IsAbove1080P);
        Assert.Equal(expected, _proxies.ShouldSuggest(probe));
    }

    [Fact]
    public void IsAbove1080P_FalseWhenProbeFailed()
    {
        Assert.False(MediaProbeResult.Unknown.IsValid);
        Assert.False(MediaProbeResult.Unknown.IsAbove1080P);
    }

    [Fact]
    public void ShouldSuggest_FalseWhenPromptDisabled()
    {
        _settings.ProxyPromptEnabled = false;
        var probe = new MediaProbeResult(3840, 2160, TimeSpan.FromHours(1), 0);
        Assert.True(probe.IsAbove1080P);
        Assert.False(_proxies.ShouldSuggest(probe));
    }

    // --- 档位解析 ---

    [Theory]
    [InlineData("720p", "720p")]
    [InlineData("540p", "540p")]
    [InlineData("360p", "360p")]
    [InlineData("540P", "540p")]
    [InlineData("banana", "720p")]
    [InlineData("", "720p")]
    [InlineData(null, "720p")]
    public void ProxyPreset_FromId_FallsBackTo720P(string? id, string expectedId)
    {
        Assert.Equal(expectedId, ProxyPreset.FromId(id).Id);
    }

    [Fact]
    public void PreferredPreset_FollowsSettings()
    {
        _settings.SetProxyPreset("360p");
        Assert.Equal("360p", _proxies.PreferredPreset.Id);
    }

    // --- 缓存路径与命中 ---

    [Fact]
    public void ResolveCacheDirectory_StableAndPerPreset()
    {
        var source = Path.Combine(_tempDir, "clip.mp4");
        File.WriteAllText(source, "stub");

        var a = _proxies.ResolveCacheDirectory(source, ProxyPreset.P720);
        var b = _proxies.ResolveCacheDirectory(source, ProxyPreset.P720);
        var c = _proxies.ResolveCacheDirectory(source, ProxyPreset.P540);

        Assert.Equal(a, b);       // 同一素材同档位 → 同一目录
        Assert.NotEqual(a, c);    // 档位不同 → 目录不同
        Assert.StartsWith(
            Path.Combine(_tempDir, "proxy"), a, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveCacheDirectory_ChangesWhenSourceFileChanges()
    {
        var source = Path.Combine(_tempDir, "clip.mp4");
        File.WriteAllText(source, "stub");
        var before = _proxies.ResolveCacheDirectory(source, ProxyPreset.P720);
        File.WriteAllText(source, "stub-changed");
        var after = _proxies.ResolveCacheDirectory(source, ProxyPreset.P720);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void TryGetExisting_FalseWhenMissingOrEmpty()
    {
        var source = Path.Combine(_tempDir, "clip.mp4");
        File.WriteAllText(source, "stub");

        Assert.False(_proxies.TryGetExisting(source, out var missing, ProxyPreset.P720));
        Assert.EndsWith("proxy.mp4", missing);

        Directory.CreateDirectory(Path.GetDirectoryName(missing)!);
        File.WriteAllText(missing, string.Empty);
        Assert.False(_proxies.TryGetExisting(source, out _, ProxyPreset.P720)); // 0 长度视为残留
    }

    [Fact]
    public void TryGetExisting_TrueAndWritesMeta()
    {
        var source = Path.Combine(_tempDir, "clip.mp4");
        File.WriteAllText(source, "stub");
        var path = _proxies.ResolveProxyPath(source, ProxyPreset.P540);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "proxy-bytes");

        Assert.True(_proxies.TryGetExisting(source, out var found, ProxyPreset.P540));
        Assert.Equal(path, found);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "meta.json")));
    }

    [Fact]
    public void EnforceCacheLimit_NoopWhenUnlimited()
    {
        _settings.ProxyCacheLimitGb = 0;
        Assert.Equal(0, _proxies.EnforceCacheLimit());
    }

    // --- 性能档位与并行分段（v1.12.0）---

    [Theory]
    [InlineData("quiet", 1)]
    [InlineData("balanced", 2)]
    [InlineData("turbo", 4)]
    [InlineData("TURBO", 4)]
    [InlineData("banana", 2)]
    [InlineData(null, 2)]
    public void ProxyPerformance_FromId_ParsesAndFallsBackToBalanced(string? id, int parallelism)
    {
        Assert.Equal(parallelism, ProxyPerformance.FromId(id).Parallelism);
    }

    [Fact]
    public void ProxyPerformance_DefaultsToBalanced()
    {
        Assert.Equal("balanced", _settings.ProxyPerformance);
    }

    [Fact]
    public void ComputeChunkRanges_SingleWhenRequestedOrTooShort()
    {
        var duration = TimeSpan.FromHours(2);
        Assert.Single(ProxyMediaService.ComputeChunkRanges(duration, 1));
        Assert.Single(ProxyMediaService.ComputeChunkRanges(duration, 0));
        Assert.Single(ProxyMediaService.ComputeChunkRanges(TimeSpan.Zero, 4));
        // 每段至少约 60 秒：90 秒素材即使请求 4 路也只分 1 段
        Assert.Single(ProxyMediaService.ComputeChunkRanges(TimeSpan.FromSeconds(90), 4));
    }

    [Fact]
    public void ComputeChunkRanges_SplitsEvenlyAndCoversFullDuration()
    {
        var duration = TimeSpan.FromHours(2);
        var ranges = ProxyMediaService.ComputeChunkRanges(duration, 4);

        Assert.Equal(4, ranges.Count);
        Assert.Equal(TimeSpan.Zero, ranges[0].Start);
        // 首尾相接、无重叠无缺口，且覆盖全长
        for (var i = 1; i < ranges.Count; i++)
            Assert.Equal(ranges[i - 1].Start + ranges[i - 1].Duration, ranges[i].Start);
        Assert.Equal(duration, ranges[^1].Start + ranges[^1].Duration);
        Assert.All(ranges, r => Assert.True(r.Duration > TimeSpan.FromMinutes(29)));
    }

    [Fact]
    public void ComputeChunkRanges_CapsByDuration()
    {
        // 200 秒素材最多 3 段（每段 ≥60 秒），请求 4 路被收敛到 3
        var ranges = ProxyMediaService.ComputeChunkRanges(TimeSpan.FromSeconds(200), 4);
        Assert.Equal(3, ranges.Count);
    }

    // --- 解码方式选择（v1.12.0 实测：H.264 软解更快，HEVC 硬解可快 2 倍以上）---

    [Theory]
    [InlineData("h264", false)]
    [InlineData("H264", false)]
    [InlineData("hevc", true)]
    [InlineData("av1", true)]
    [InlineData("vp9", true)]
    [InlineData(null, false)]
    public void PreferHardwareDecode_H264UsesSoftwareOthersHardware(string? codec, bool expected)
    {
        var probe = new MediaProbeResult(3840, 2160, TimeSpan.FromHours(1), 0, codec);
        Assert.Equal(expected, probe.PreferHardwareDecode);
    }
}
