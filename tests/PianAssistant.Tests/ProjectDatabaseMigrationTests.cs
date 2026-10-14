using System.IO;
using Microsoft.Data.Sqlite;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// M7 schema v1 → v2 迁移测试：新增素材会话表，幂等且不破坏既有数据。
/// </summary>
public class ProjectDatabaseMigrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public ProjectDatabaseMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-mig-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private static HashSet<string> GetTableNames(ProjectDatabase db)
    {
        var names = new HashSet<string>();
        using var conn = db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
    }

    [Fact]
    public void EnsureInitialized_MigratesToCurrentVersion()
    {
        var db = new ProjectDatabase(_dbPath);
        db.EnsureInitialized();
        Assert.Equal(ProjectDatabase.CurrentSchemaVersion, db.GetSchemaVersion());
    }

    [Fact]
    public void Migrate_CreatesMediaSessionTables()
    {
        var db = new ProjectDatabase(_dbPath);
        db.EnsureInitialized();
        var tables = GetTableNames(db);

        Assert.Contains("media_sessions", tables);
        Assert.Contains("session_cut_points", tables);
        Assert.Contains("session_screenshots", tables);
        Assert.Contains("session_notes", tables);
    }

    [Fact]
    public void MigrateIfNeeded_FromV1_UpgradesAndPreservesExistingData()
    {
        // 仅初始化到 v1，写入一条项目数据。
        var db = new ProjectDatabase(_dbPath);
        db.Initialize();
        Assert.Equal(1, db.GetSchemaVersion());

        var repo = new ProjectRepository(db);
        var project = repo.CreateProject("迁移前的项目", "v.mp4");
        Assert.NotNull(project);

        // 迁移 v1 → v2。
        db.MigrateIfNeeded();
        Assert.Equal(ProjectDatabase.CurrentSchemaVersion, db.GetSchemaVersion());

        // 既有数据完好。
        var loaded = repo.GetProject(project!.Id.ToString());
        Assert.NotNull(loaded);
        Assert.Equal("迁移前的项目", loaded!.Name);
        // 会话表已就绪。
        Assert.Contains("media_sessions", GetTableNames(db));
    }

    [Fact]
    public void MigrateIfNeeded_IsIdempotent()
    {
        var db = new ProjectDatabase(_dbPath);
        db.Initialize();
        db.MigrateIfNeeded();
        db.MigrateIfNeeded();
        db.MigrateIfNeeded();
        Assert.Equal(ProjectDatabase.CurrentSchemaVersion, db.GetSchemaVersion());
        Assert.Contains("media_sessions", GetTableNames(db));
    }

    [Fact]
    public void Migrate_DoesNotDropBaseTables()
    {
        var db = new ProjectDatabase(_dbPath);
        db.EnsureInitialized();
        var tables = GetTableNames(db);

        Assert.Contains("projects", tables);
        Assert.Contains("screenshots", tables);
        Assert.Contains("clips", tables);
        Assert.Contains("notes", tables);
        Assert.Contains("schema_version", tables);
    }
}
