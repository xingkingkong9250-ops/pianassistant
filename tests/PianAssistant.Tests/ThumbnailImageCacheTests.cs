using System.IO;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// 缩略图缓存测试：命中复用、按宽度区分、LRU 淘汰、清空、缺失文件容错。
/// 用内嵌的 1×1 最小合法 PNG 解码，避免依赖外部资源。
/// </summary>
public class ThumbnailImageCacheTests : IDisposable
{
    // 1×1 透明 PNG（合法且体积最小），供解码用。
    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

    private readonly string _tempDir;

    public ThumbnailImageCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-thumb-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string WritePng(string name)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, Convert.FromBase64String(OnePixelPngBase64));
        return path;
    }

    [Fact]
    public void Get_SamePathAndWidth_ReturnsSameInstance()
    {
        var cache = new ThumbnailImageCache(4);
        var path = WritePng("a.png");

        var first = cache.Get(path, 200);
        var second = cache.Get(path, 200);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Get_DifferentWidth_ReturnsDistinctInstances()
    {
        var cache = new ThumbnailImageCache(4);
        var path = WritePng("b.png");

        var w200 = cache.Get(path, 200);
        var w100 = cache.Get(path, 100);

        Assert.NotNull(w200);
        Assert.NotNull(w100);
        Assert.NotSame(w200, w100);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Get_MissingFile_ReturnsNull_AndDoesNotCache()
    {
        var cache = new ThumbnailImageCache(4);
        var missing = Path.Combine(_tempDir, "nope.png");

        Assert.Null(cache.Get(missing, 200));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Get_BlankPath_ReturnsNull()
    {
        var cache = new ThumbnailImageCache(4);
        Assert.Null(cache.Get("  ", 200));
    }

    [Fact]
    public void Get_ExceedingCapacity_EvictsLeastRecentlyUsed()
    {
        var cache = new ThumbnailImageCache(2);
        var a = WritePng("a.png");
        var b = WritePng("b.png");
        var c = WritePng("c.png");

        var aFirst = cache.Get(a, 200);
        var bFirst = cache.Get(b, 200);
        // 再次读取 A：A 提升为最近使用，B 成为最久未用。
        Assert.Same(aFirst, cache.Get(a, 200));

        cache.Get(c, 200); // 超容量 → 淘汰最久未用的 B

        Assert.Equal(2, cache.Count);
        Assert.Same(aFirst, cache.Get(a, 200));    // A 仍在缓存，复用同一实例
        Assert.NotSame(bFirst, cache.Get(b, 200)); // B 被淘汰后重新解码
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var cache = new ThumbnailImageCache(4);
        cache.Get(WritePng("a.png"), 200);
        cache.Get(WritePng("b.png"), 200);
        Assert.Equal(2, cache.Count);

        cache.Clear();
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void DecodeUncached_ReturnsImage_ButDoesNotPopulateCache()
    {
        var cache = new ThumbnailImageCache(4);
        var path = WritePng("d.png");

        var image = ThumbnailImageCache.DecodeUncached(path);

        Assert.NotNull(image);
        Assert.Equal(0, cache.Count); // 全分辨率解码不进入缓存
    }

    [Fact]
    public void Constructor_NonPositiveCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThumbnailImageCache(0));
    }
}
