using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// M7 素材身份指纹测试：快速键（路径+大小+mtime）与内容哈希（SHA-256 流式）。
/// </summary>
public class ContentFingerprintTests : IDisposable
{
    private readonly string _tempDir;

    public ContentFingerprintTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-fp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    // --- 快速键 ---

    [Fact]
    public void ComputeFastKey_ExistingFile_ReturnsPathSizeMtime()
    {
        var path = WriteFile("a.bin", new byte[] { 1, 2, 3, 4 });
        var key = ContentFingerprint.ComputeFastKey(path);

        Assert.NotNull(key);
        var fi = new FileInfo(path);
        Assert.Equal($"{path}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}", key);
    }

    [Fact]
    public void ComputeFastKey_MissingFile_ReturnsNull()
    {
        var path = Path.Combine(_tempDir, "does-not-exist.bin");
        Assert.Null(ContentFingerprint.ComputeFastKey(path));
    }

    [Fact]
    public void ComputeFastKey_BlankPath_ReturnsNull()
    {
        Assert.Null(ContentFingerprint.ComputeFastKey("  "));
    }

    [Fact]
    public void ComputeFastKey_ChangesWhenSizeChanges()
    {
        var path = Path.Combine(_tempDir, "grow.bin");
        File.WriteAllBytes(path, new byte[] { 1 });
        var before = ContentFingerprint.ComputeFastKey(path);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        var after = ContentFingerprint.ComputeFastKey(path);

        Assert.NotEqual(before, after);
    }

    // --- 内容哈希 ---

    [Fact]
    public async Task ComputeContentHashAsync_EmptyFile_MatchesKnownSha256()
    {
        var path = WriteFile("empty.bin", Array.Empty<byte>());
        var hash = await ContentFingerprint.ComputeContentHashAsync(path);

        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash);
    }

    [Fact]
    public async Task ComputeContentHashAsync_KnownContent_MatchesExpected()
    {
        var path = WriteFile("abc.bin", System.Text.Encoding.ASCII.GetBytes("abc"));
        var hash = await ContentFingerprint.ComputeContentHashAsync(path);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }

    [Fact]
    public async Task ComputeContentHashAsync_SameContentDifferentPath_EqualHashes()
    {
        var content = System.Text.Encoding.ASCII.GetBytes("same bytes here");
        var a = WriteFile("a.bin", content);
        var b = WriteFile("b.bin", content);

        var ha = await ContentFingerprint.ComputeContentHashAsync(a);
        var hb = await ContentFingerprint.ComputeContentHashAsync(b);

        Assert.NotNull(ha);
        Assert.Equal(ha, hb);
    }

    [Fact]
    public async Task ComputeContentHashAsync_LargeFileBeyondOneChunk_MatchesReference()
    {
        // 超过 1MB 分块，验证流式分块拼接结果与一次性哈希一致。
        var content = new byte[3 * 1024 * 1024 + 123];
        new Random(12345).NextBytes(content);
        var path = WriteFile("large.bin", content);

        var hash = await ContentFingerprint.ComputeContentHashAsync(path);
        var expected = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        Assert.Equal(expected, hash);
    }

    [Fact]
    public async Task ComputeContentHashAsync_MissingFile_ReturnsNull()
    {
        var path = Path.Combine(_tempDir, "nope.bin");
        Assert.Null(await ContentFingerprint.ComputeContentHashAsync(path));
    }

    [Fact]
    public async Task ComputeContentHashAsync_Cancelled_ReturnsNull()
    {
        var path = WriteFile("cancel.bin", new byte[1024]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        // 取消令牌贯穿；实现将其视为失败并安全返回 null（不抛出）。
        var hash = await ContentFingerprint.ComputeContentHashAsync(path, cts.Token);
        Assert.Null(hash);
    }
}
