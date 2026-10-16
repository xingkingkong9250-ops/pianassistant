using System.IO;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// 片段缩略图服务：ffmpeg 抽段起始帧缓存到 clips/thumbs/{startMs}.jpg，
/// 已存在直接命中，缺失异步补抽；孤儿缓存可按当前段集合清理。
/// </summary>
public class SegmentThumbnailServiceTests : IDisposable
{
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
    public void GetThumbnailPath_UsesStartMilliseconds()
    {
        var svc = new SegmentThumbnailService(_tempDir, FakeCapture());
        var path = svc.GetThumbnailPath(TimeSpan.FromSeconds(90));
        Assert.Equal(Path.Combine(_tempDir, "thumbs", "90000.jpg"), path);
    }

    [Fact]
    public async Task EnsureThumbnail_CreatesCacheFile()
    {
        var svc = new SegmentThumbnailService(_tempDir, FakeCapture());
        var path = await svc.EnsureThumbnailAsync("video.mp4", TimeSpan.FromSeconds(10));
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Equal("10000.jpg", Path.GetFileName(path));
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
        var first = await svc.EnsureThumbnailAsync("video.mp4", TimeSpan.FromSeconds(10));
        var second = await svc.EnsureThumbnailAsync("video.mp4", TimeSpan.FromSeconds(10));
        Assert.Equal(first, second);
        Assert.Equal(1, calls); // 第二次命中缓存，不再抽帧
    }

    [Fact]
    public async Task EnsureThumbnail_CaptureFails_ReturnsNull()
    {
        var svc = new SegmentThumbnailService(_tempDir,
            (s, p, o) => Task.FromException<string>(new InvalidOperationException("boom")));
        var path = await svc.EnsureThumbnailAsync("video.mp4", TimeSpan.FromSeconds(10));
        Assert.Null(path);
    }

    [Fact]
    public async Task PruneOrphans_DeletesFilesNotInValidSet()
    {
        var svc = new SegmentThumbnailService(_tempDir, FakeCapture());
        await svc.EnsureThumbnailAsync("video.mp4", TimeSpan.FromSeconds(10));
        await svc.EnsureThumbnailAsync("video.mp4", TimeSpan.FromSeconds(20));
        await svc.EnsureThumbnailAsync("video.mp4", TimeSpan.FromSeconds(30));

        var deleted = svc.PruneOrphans(new HashSet<long> { 10000, 30000 });

        Assert.Equal(1, deleted);
        Assert.True(File.Exists(svc.GetThumbnailPath(TimeSpan.FromSeconds(10))));
        Assert.False(File.Exists(svc.GetThumbnailPath(TimeSpan.FromSeconds(20))));
        Assert.True(File.Exists(svc.GetThumbnailPath(TimeSpan.FromSeconds(30))));
    }
}
