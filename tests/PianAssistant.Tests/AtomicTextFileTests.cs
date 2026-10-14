using System.IO;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// 原子文本写/恢复测试：写入、备份快照、缺失/损坏时从备份恢复、无快照时的异常。
/// AtomicTextFile 为 internal，经 InternalsVisibleTo 访问。
/// </summary>
public class AtomicTextFileTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _path;

    public AtomicTextFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-atomic-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _path = Path.Combine(_tempDir, "state.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void WriteAllText_CreatesFile()
    {
        AtomicTextFile.WriteAllText(_path, "{\"a\":1}");
        Assert.True(File.Exists(_path));
        Assert.Equal("{\"a\":1}", File.ReadAllText(_path));
    }

    [Fact]
    public void WriteAllText_SecondWrite_CreatesBackupOfPreviousContent()
    {
        AtomicTextFile.WriteAllText(_path, "first");
        AtomicTextFile.WriteAllText(_path, "second");

        var backup = AtomicTextFile.GetBackupPath(_path);
        Assert.True(File.Exists(backup));
        Assert.Equal("first", File.ReadAllText(backup));
        Assert.Equal("second", File.ReadAllText(_path));
    }

    [Fact]
    public void ReadAllTextOrRecover_ReturnsFileContent()
    {
        File.WriteAllText(_path, "hello");
        Assert.Equal("hello", AtomicTextFile.ReadAllTextOrRecover(_path));
    }

    [Fact]
    public void ReadAllTextOrRecover_MissingFileWithBackup_ReturnsBackupContent()
    {
        AtomicTextFile.WriteAllText(_path, "v1");
        AtomicTextFile.WriteAllText(_path, "v2"); // 生成 backup=v1
        File.Delete(_path);

        Assert.Equal("v1", AtomicTextFile.ReadAllTextOrRecover(_path));
    }

    [Fact]
    public void ReadAllTextOrRecover_NoFileNoBackup_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => AtomicTextFile.ReadAllTextOrRecover(_path));
    }

    [Fact]
    public void RestoreBackup_RestoresBackupContent()
    {
        AtomicTextFile.WriteAllText(_path, "v1");
        AtomicTextFile.WriteAllText(_path, "v2");

        AtomicTextFile.RestoreBackup(_path);

        Assert.Equal("v1", File.ReadAllText(_path));
    }

    [Fact]
    public void RestoreBackup_NoBackup_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => AtomicTextFile.RestoreBackup(_path));
    }
}
