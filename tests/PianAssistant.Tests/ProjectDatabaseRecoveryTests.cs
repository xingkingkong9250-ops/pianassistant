using System.IO;
using Microsoft.Data.Sqlite;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// 数据库损坏自愈测试：健康库不自愈；损坏库被隔离为 *.corrupt 并重建空库，
/// 原始损坏文件被完整保留（绝不静默丢弃）。
/// </summary>
public class ProjectDatabaseRecoveryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public ProjectDatabaseRecoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-db-recover-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
    }

    public void Dispose()
    {
        // 释放连接池以解除文件锁，再清理临时目录。
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void EnsureInitialized_HealthyDatabase_NoRecovery()
    {
        var db = new ProjectDatabase(_dbPath);
        var result = db.EnsureInitialized();

        Assert.True(result.Success);
        Assert.False(result.Recovered);
        Assert.Null(result.Message);
        // EnsureInitialized 完成全量迁移（M7 起为 v2）。
        Assert.Equal(ProjectDatabase.CurrentSchemaVersion, db.GetSchemaVersion());
    }

    [Fact]
    public void EnsureInitialized_CorruptFile_RecoversAndRebuilds()
    {
        // 写入随机字节模拟损坏的数据库文件。
        File.WriteAllBytes(_dbPath, new byte[] { 0x00, 0x01, 0x02, 0x03, 0xDE, 0xAD, 0xBE, 0xEF });
        SqliteConnection.ClearAllPools();

        var db = new ProjectDatabase(_dbPath);
        var result = db.EnsureInitialized();

        Assert.True(result.Success);
        Assert.True(result.Recovered);
        Assert.NotNull(result.Message);
        // 空库已重建并可用（含 M7 会话表迁移）。
        Assert.Equal(ProjectDatabase.CurrentSchemaVersion, db.GetSchemaVersion());
    }

    [Fact]
    public void EnsureInitialized_CorruptFile_PreservesOriginalAsCorrupt()
    {
        var original = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0x11, 0x22, 0x33, 0x44 };
        File.WriteAllBytes(_dbPath, original);

        var db = new ProjectDatabase(_dbPath);
        var result = db.EnsureInitialized();

        Assert.True(result.Recovered);
        var corruptPath = _dbPath + ".corrupt";
        Assert.True(File.Exists(corruptPath), "损坏的原始库应被保留为 *.corrupt");
        Assert.Equal(original, File.ReadAllBytes(corruptPath));
    }
}
