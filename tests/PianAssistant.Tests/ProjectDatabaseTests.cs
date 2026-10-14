using System.IO;
using Microsoft.Data.Sqlite;
using PianAssistant.Models;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// M3 持久化测试：SQLite schema 初始化、ProjectRepository 各表 CRUD、级联删除。
/// 每个测试用独立临时目录隔离数据库文件，Dispose 中释放连接池以便清理。
/// </summary>
public class ProjectDatabaseTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public ProjectDatabaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-db-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
    }

    public void Dispose()
    {
        // 释放 SQLite 连接池以解除文件锁，再清理临时目录。
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // -------- 辅助方法 --------

    private (ProjectDatabase db, ProjectRepository repo) CreateDb()
    {
        var db = new ProjectDatabase(_dbPath);
        db.Initialize();
        db.MigrateIfNeeded();
        return (db, new ProjectRepository(db));
    }

    /// <summary>查询 sqlite_master 获取所有用户表名。</summary>
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

    // -------- Initialize 测试 --------

    [Fact]
    public void Initialize_CreatesAllTables()
    {
        var (db, _) = CreateDb();
        var tables = GetTableNames(db);

        Assert.Contains("schema_version", tables);
        Assert.Contains("projects", tables);
        Assert.Contains("screenshots", tables);
        Assert.Contains("clips", tables);
        Assert.Contains("notes", tables);
    }

    [Fact]
    public void Initialize_WritesSchemaVersion1()
    {
        // Initialize 仅建立 v1 基础表并写入 schema_version=1；
        // v2 表由 MigrateIfNeeded 增量迁移（见 MigrateIfNeeded_* 测试）。
        var db = new ProjectDatabase(_dbPath);
        db.Initialize();
        Assert.Equal(1, db.GetSchemaVersion());
    }

    [Fact]
    public void Initialize_IsIdempotent()
    {
        var db = new ProjectDatabase(_dbPath);
        db.Initialize();
        // 第二次调用不应报错，版本号仍为 1。
        db.Initialize();
        Assert.Equal(1, db.GetSchemaVersion());
    }

    [Fact]
    public void MigrateIfNeeded_AtLatest_DoesNothing()
    {
        var (db, _) = CreateDb();
        // CreateDb 已完成迁移（当前 schema 版本），再次迁移应保持不变。
        db.MigrateIfNeeded();
        Assert.Equal(ProjectDatabase.CurrentSchemaVersion, db.GetSchemaVersion());
    }

    // -------- 项目 CRUD 测试 --------

    [Fact]
    public void CreateProject_ReturnsProjectWithId()
    {
        var (_, repo) = CreateDb();
        var project = repo.CreateProject("测试项目", @"C:\video\test.mp4");
        Assert.NotNull(project);
        Assert.NotEqual(Guid.Empty, project!.Id);
        Assert.Equal("测试项目", project.Name);
        Assert.Equal(@"C:\video\test.mp4", project.SourcePath);
    }

    [Fact]
    public void GetProject_ReturnsCreatedProject()
    {
        var (_, repo) = CreateDb();
        var created = repo.CreateProject("拉片A", @"D:\media\a.mkv");
        Assert.NotNull(created);

        var loaded = repo.GetProject(created!.Id.ToString());
        Assert.NotNull(loaded);
        Assert.Equal(created.Id, loaded!.Id);
        Assert.Equal("拉片A", loaded.Name);
        Assert.Equal(@"D:\media\a.mkv", loaded.SourcePath);
    }

    [Fact]
    public void GetProject_NotExist_ReturnsNull()
    {
        var (_, repo) = CreateDb();
        var loaded = repo.GetProject(Guid.NewGuid().ToString());
        Assert.Null(loaded);
    }

    [Fact]
    public void GetAllProjects_ReturnsAllSortedByUpdatedAtDesc()
    {
        var (_, repo) = CreateDb();
        var p1 = repo.CreateProject("P1", "a.mp4");
        var p2 = repo.CreateProject("P2", "b.mp4");
        Assert.NotNull(p1);
        Assert.NotNull(p2);
        // 更新 P1 使其 updated_at 更晚。
        p1!.Name = "P1-updated";
        repo.UpdateProject(p1);

        var all = repo.GetAllProjects();
        Assert.Equal(2, all.Count);
        // P1 更新过，应排在前面。
        Assert.Equal("P1-updated", all[0].Name);
    }

    [Fact]
    public void UpdateProject_PersistsChanges()
    {
        var (_, repo) = CreateDb();
        var created = repo.CreateProject("原名称", "src.mp4");
        Assert.NotNull(created);

        created!.Name = "新名称";
        created.SourcePath = "new.mp4";
        repo.UpdateProject(created);

        var loaded = repo.GetProject(created.Id.ToString());
        Assert.NotNull(loaded);
        Assert.Equal("新名称", loaded!.Name);
        Assert.Equal("new.mp4", loaded.SourcePath);
    }

    // -------- 截图 CRUD 测试 --------

    [Fact]
    public void AddScreenshot_GetScreenshots_RoundTrip()
    {
        var (_, repo) = CreateDb();
        var project = repo.CreateProject("截图项目", "video.mp4");
        Assert.NotNull(project);

        var entry = new ScreenshotEntry(
            "video.mp4", @"C:\out\shot1.png",
            TimeSpan.FromMilliseconds(5000), DateTime.Now, "关键帧");
        repo.AddScreenshot(entry, project!.Id.ToString());

        var second = new ScreenshotEntry(
            "video.mp4", @"C:\out\shot2.png",
            TimeSpan.FromMilliseconds(2000), DateTime.Now, null);
        repo.AddScreenshot(second, project.Id.ToString());

        var loaded = repo.GetScreenshots(project.Id.ToString());
        Assert.Equal(2, loaded.Count);
        // 按位置升序，shot2(2000ms) 应在前。
        Assert.Equal(2000, loaded[0].Position.TotalMilliseconds);
        Assert.Equal(5000, loaded[1].Position.TotalMilliseconds);
        Assert.Equal("关键帧", loaded[1].Label);
        Assert.NotEmpty(loaded[0].Id!); // 从 DB 加载后应有 Id
    }

    [Fact]
    public void DeleteScreenshot_RemovesRecord()
    {
        var (_, repo) = CreateDb();
        var project = repo.CreateProject("删截图", "v.mp4");
        Assert.NotNull(project);

        var entry = new ScreenshotEntry(
            "v.mp4", "out.png", TimeSpan.FromSeconds(1), DateTime.Now);
        repo.AddScreenshot(entry, project!.Id.ToString());

        var loaded = repo.GetScreenshots(project.Id.ToString());
        Assert.Single(loaded);
        repo.DeleteScreenshot(loaded[0].Id!);
        Assert.Empty(repo.GetScreenshots(project.Id.ToString()));
    }

    // -------- 片段 CRUD 测试 --------

    [Fact]
    public void AddClip_GetClips_RoundTrip()
    {
        var (_, repo) = CreateDb();
        var project = repo.CreateProject("片段项目", "video.mkv");
        Assert.NotNull(project);

        var clip = new ClipRange("video.mkv",
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), "镜头一");
        repo.AddClip(clip, @"C:\out\clip1.mp4", project!.Id.ToString());

        var loaded = repo.GetClips(project.Id.ToString());
        Assert.Single(loaded);
        Assert.Equal(10000, loaded[0].InPoint.TotalMilliseconds);
        Assert.Equal(20000, loaded[0].OutPoint!.Value.TotalMilliseconds);
        Assert.Equal("镜头一", loaded[0].Label);
        Assert.NotEmpty(loaded[0].Id!);
    }

    [Fact]
    public void GetClipsWithOutput_ReturnsOutputPath()
    {
        var (_, repo) = CreateDb();
        var project = repo.CreateProject("导出测试", "v.mkv");
        Assert.NotNull(project);

        var clip = new ClipRange("v.mkv",
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
        repo.AddClip(clip, @"C:\clips\out.mp4", project!.Id.ToString());

        var loaded = repo.GetClipsWithOutput(project.Id.ToString());
        Assert.Single(loaded);
        Assert.Equal(@"C:\clips\out.mp4", loaded[0].outputPath);
    }

    [Fact]
    public void AddClip_NullOutPoint_Persists()
    {
        var (_, repo) = CreateDb();
        var project = repo.CreateProject("未完成片段", "v.mp4");
        Assert.NotNull(project);

        var clip = new ClipRange("v.mp4", TimeSpan.FromSeconds(3));
        repo.AddClip(clip, string.Empty, project!.Id.ToString());

        var loaded = repo.GetClips(project.Id.ToString());
        Assert.Single(loaded);
        Assert.Null(loaded[0].OutPoint);
    }

    // -------- 笔记 CRUD 测试 --------

    [Fact]
    public void AddNote_GetNotes_RoundTrip()
    {
        var (_, repo) = CreateDb();
        var project = repo.CreateProject("笔记项目", "v.mp4");
        Assert.NotNull(project);

        var note = new NoteEntry("v.mp4",
            TimeSpan.FromSeconds(30), "此处注意光影", DateTime.Now);
        repo.AddNote(note, project!.Id.ToString());

        var loaded = repo.GetNotes(project.Id.ToString());
        Assert.Single(loaded);
        Assert.Equal("此处注意光影", loaded[0].Text);
        Assert.Equal(30000, loaded[0].Position.TotalMilliseconds);
        Assert.NotEmpty(loaded[0].Id!);
    }

    [Fact]
    public void UpdateNote_UpdatesTextAndTimestamp()
    {
        var (_, repo) = CreateDb();
        var project = repo.CreateProject("更新笔记", "v.mp4");
        Assert.NotNull(project);

        var note = new NoteEntry("v.mp4", TimeSpan.FromSeconds(5), "原始", DateTime.Now);
        repo.AddNote(note, project!.Id.ToString());

        var loaded = repo.GetNotes(project.Id.ToString());
        Assert.Single(loaded);
        loaded[0].Text = "修改后";
        repo.UpdateNote(loaded[0]);

        var after = repo.GetNotes(project.Id.ToString());
        Assert.Single(after);
        Assert.Equal("修改后", after[0].Text);
        Assert.NotNull(after[0].UpdatedAt);
    }

    [Fact]
    public void DeleteNote_RemovesRecord()
    {
        var (_, repo) = CreateDb();
        var project = repo.CreateProject("删笔记", "v.mp4");
        Assert.NotNull(project);

        var note = new NoteEntry("v.mp4", TimeSpan.FromSeconds(1), "待删", DateTime.Now);
        repo.AddNote(note, project!.Id.ToString());

        var loaded = repo.GetNotes(project.Id.ToString());
        Assert.Single(loaded);
        repo.DeleteNote(loaded[0].Id!);
        Assert.Empty(repo.GetNotes(project.Id.ToString()));
    }

    // -------- 级联删除测试 --------

    [Fact]
    public void DeleteProject_CascadesToScreenshotsClipsNotes()
    {
        var (_, repo) = CreateDb();
        var project = repo.CreateProject("级联删除", "v.mp4");
        Assert.NotNull(project);
        var pid = project!.Id.ToString();

        // 添加各类数据。
        repo.AddScreenshot(new ScreenshotEntry("v.mp4", "s.png",
            TimeSpan.FromSeconds(1), DateTime.Now), pid);
        repo.AddClip(new ClipRange("v.mp4",
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)), "c.mp4", pid);
        repo.AddNote(new NoteEntry("v.mp4",
            TimeSpan.FromSeconds(4), "笔记", DateTime.Now), pid);

        // 验证数据存在。
        Assert.Single(repo.GetScreenshots(pid));
        Assert.Single(repo.GetClips(pid));
        Assert.Single(repo.GetNotes(pid));

        // 删除项目。
        repo.DeleteProject(pid);

        // 验证级联删除。
        Assert.Empty(repo.GetScreenshots(pid));
        Assert.Empty(repo.GetClips(pid));
        Assert.Empty(repo.GetNotes(pid));
        Assert.Null(repo.GetProject(pid));
    }
}
