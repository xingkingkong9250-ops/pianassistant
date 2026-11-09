using System.IO;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// 片段缩略图服务：ffmpeg 抽段起始帧缓存到 clips/thumbs/{源视频键}_{startMs}.jpg，
/// 已存在直接命中，缺失异步补抽；孤儿缓存可按当前段集合清理。
/// </summary>
public class SegmentThumbnailServiceTests : IDisposable
{
    private const string Source = "video.mp4";
    private readonly string _tempDir;

    public SegmentThumbnailServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-thumbs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    /// <summary>假抽帧：直接写一个占位 jpg，返回输出路径。</summary>
    private static Func<string, TimeSpan, string, Task<string>> FakeCapture(int delayMs = 0) =>
        (source, position, outputPath) => Task.Run(() =>
        {
            if (delayMs > 0) Thread.Sleep(delayMs);
            File.WriteAllBytes(outputPath, new byte[] { 0xFF, 0xD8, 0xFF });
            return outputPath;
        });

    [Fact]
    public void GetThumbnailPath_ContainsSourceKeyAndStartMilliseconds()
    {
        var svc = new SegmentThumbnailService(_tempDir, FakeCapture());
        var path = svc.GetThumbnailPath(Source, TimeSpan.FromSeconds(90));
        Assert.Equal(Path.Combine(_tempDir, "thumbs"), Path.GetDirectoryName(path));
        Assert.EndsWith("_90000.jpg", path);
        Assert.StartsWith("video_", Path.GetFileName(path));
    }

    [Fact]
    public void GetThumbnailPath_DifferentSources_SameStart_AreIsolated()
    {
        // v1.15.8 回归：旧实现只按 startMs 命名，两个视频同一毫秒位置共用缓存，
        // 切片后笔记列表会显示上一个视频的帧
        var svc = new SegmentThumbnailService(_tempDir, FakeCapture());
        var a = svc.GetThumbnailPath(@"C:\media\a.mp4", TimeSpan.FromSeconds(10));
        var b = svc.GetThumbnailPath(@"C:\media\b.mp4", TimeSpan.FromSeconds(10));
        Assert.NotEqual(a, b);
        Assert.EndsWith("_10000.jpg", a);
        Assert.EndsWith("_10000.jpg", b);
    }

    [Fact]
    public async Task EnsureThumbnail_CreatesCacheFile()
    {
        var svc = new SegmentThumbnailService(_tempDir, FakeCapture());
        var path = await svc.EnsureThumbnailAsync(Source, TimeSpan.FromSeconds(10));
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.EndsWith("_10000.jpg", path);
    }

    [Fact]
    public async Task EnsureThumbnail_CacheHit_SkipsCapture()
    {
        var calls = 0;
        var svc = new SegmentThumbnailService(_tempDir, (s, p, o) =>
        {
            calls++;
            return FakeCapture()(s, p, o);
        });
        var first = await svc.EnsureThumbnailAsync(Source, TimeSpan.FromSeconds(10));
        var second = await svc.EnsureThumbnailAsync(Source, TimeSpan.FromSeconds(10));
        Assert.Equal(first, second);
        Assert.Equal(1, calls); // 第二次命中缓存，不再抽帧
    }

    [Fact]
    public async Task EnsureThumbnail_CaptureFails_ReturnsNull()
    {
        var svc = new SegmentThumbnailService(_tempDir,
            (s, p, o) => Task.FromException<string>(new InvalidOperationException("boom")));
        var path = await svc.EnsureThumbnailAsync(Source, TimeSpan.FromSeconds(10));
        Assert.Null(path);
    }

    [Fact]
    public async Task PruneOrphans_DeletesFilesNotInValidSet()
    {
        var svc = new SegmentThumbnailService(_tempDir, FakeCapture());
        await svc.EnsureThumbnailAsync(Source, TimeSpan.FromSeconds(10));
        await svc.EnsureThumbnailAsync(Source, TimeSpan.FromSeconds(20));
        await svc.EnsureThumbnailAsync(Source, TimeSpan.FromSeconds(30));

        var deleted = svc.PruneOrphans(Source, new HashSet<long> { 10000, 30000 });

        Assert.Equal(1, deleted);
        Assert.True(File.Exists(svc.GetThumbnailPath(Source, TimeSpan.FromSeconds(10))));
        Assert.False(File.Exists(svc.GetThumbnailPath(Source, TimeSpan.FromSeconds(20))));
        Assert.True(File.Exists(svc.GetThumbnailPath(Source, TimeSpan.FromSeconds(30))));
    }

    [Fact]
    public async Task PruneOrphans_KeepsOtherSourcesAndRemovesLegacyNumericCache()
    {
        var svc = new SegmentThumbnailService(_tempDir, FakeCapture());
        await svc.EnsureThumbnailAsync(@"C:\media\a.mp4", TimeSpan.FromSeconds(10));
        await svc.EnsureThumbnailAsync(@"C:\media\b.mp4", TimeSpan.FromSeconds(10));
        // 旧版本遗留的纯毫秒命名缓存（跨视频共享，属错误数据）
        var legacy = Path.Combine(_tempDir, "thumbs", "10000.jpg");
        File.WriteAllBytes(legacy, new byte[] { 0xFF, 0xD8, 0xFF });

        var deleted = svc.PruneOrphans(@"C:\media\a.mp4", new HashSet<long> { 10000 });

        Assert.Equal(1, deleted); // 仅删除旧格式文件
        Assert.False(File.Exists(legacy));
        Assert.True(File.Exists(svc.GetThumbnailPath(@"C:\media\a.mp4", TimeSpan.FromSeconds(10))));
        Assert.True(File.Exists(svc.GetThumbnailPath(@"C:\media\b.mp4", TimeSpan.FromSeconds(10))));
    }
}
