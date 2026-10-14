using System.IO;
using Microsoft.Data.Sqlite;
using PianAssistant.Models;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// M7 素材会话仓储 CRUD 测试：两种键查询、明细整体替换、哈希唯一与删除级联。
/// </summary>
public class MediaSessionRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ProjectDatabase _db;
    private readonly ProjectRepository _repo;

    public MediaSessionRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-sess-repo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _db = new ProjectDatabase(Path.Combine(_tempDir, "test.db"));
        _db.EnsureInitialized();
        _repo = new ProjectRepository(_db);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private static MediaSession BuildSession(string fastKey, string? hash = null,
        int cutPoints = 0, int screenshots = 0, int notes = 0)
    {
        var session = new MediaSession
        {
            FastKey = fastKey,
            ContentHash = hash,
            Path = @"C:\media\video.mp4",
            Size = 1234,
            MtimeUtcTicks = 999,
            LastPositionMs = 42000,
            Rate = 1.5,
            InPointMs = 1000,
            OutPointMs = 5000,
            UpdatedAt = DateTime.Now,
        };
        for (int i = 0; i < cutPoints; i++)
            session.CutPointsMs.Add(1000 * (i + 1));
        for (int i = 0; i < screenshots; i++)
            session.Screenshots.Add(new SessionScreenshot
            {
                FilePath = $@"C:\shots\s{i}.png",
                PositionMs = 1000 * (i + 1),
                CapturedAt = DateTime.Now,
                Label = $"L{i}",
            });
        for (int i = 0; i < notes; i++)
            session.Notes.Add(new SessionNote
            {
                PositionMs = 1000 * (i + 1),
                Text = $"note{i}",
                CreatedAt = DateTime.Now,
            });
        return session;
    }

    [Fact]
    public void UpsertSession_LoadByFastKey_RoundTripsAllFields()
    {
        var session = BuildSession("video.mp4|1234|999", cutPoints: 2, screenshots: 1, notes: 3);
        _repo.UpsertSession(session);

        var loaded = _repo.LoadSessionByFastKey("video.mp4|1234|999");
        Assert.NotNull(loaded);
        Assert.Equal(@"C:\media\video.mp4", loaded!.Path);
        Assert.Equal(42000, loaded.LastPositionMs);
        Assert.Equal(1.5, loaded.Rate, 3);
        Assert.Equal(1000, loaded.InPointMs);
        Assert.Equal(5000, loaded.OutPointMs);
        Assert.Equal(2, loaded.CutPointsMs.Count);
        Assert.Single(loaded.Screenshots);
        Assert.Equal(3, loaded.Notes.Count);
        Assert.Equal("note0", loaded.Notes[0].Text);
    }

    [Fact]
    public void LoadSessionByFastKey_NotExist_ReturnsNull()
    {
        Assert.Null(_repo.LoadSessionByFastKey("missing|0|0"));
    }

    [Fact]
    public void UpsertSession_ReplacesDetailsInsteadOfAppending()
    {
        var key = "k|1|1";
        _repo.UpsertSession(BuildSession(key, cutPoints: 3, notes: 2));
        // 再次写入同一快速键：明细应整体替换为 1，而非累加。
        _repo.UpsertSession(BuildSession(key, cutPoints: 1, notes: 1));

        var loaded = _repo.LoadSessionByFastKey(key);
        Assert.NotNull(loaded);
        Assert.Single(loaded!.CutPointsMs);
        Assert.Single(loaded.Notes);
    }

    [Fact]
    public void LoadSessionByHash_ReturnsSession()
    {
        _repo.UpsertSession(BuildSession("a|1|1", hash: "deadbeef", cutPoints: 1));

        var loaded = _repo.LoadSessionByHash("deadbeef");
        Assert.NotNull(loaded);
        Assert.Equal("a|1|1", loaded!.FastKey);
        Assert.Equal("deadbeef", loaded.ContentHash);
    }

    [Fact]
    public void UpsertSession_HashAlreadyTaken_ByOtherKey_NullsThisRowHash()
    {
        // 唯一索引：同一 content_hash 只能由一行持有；后写入行哈希被置空，旧行保留。
        _repo.UpsertSession(BuildSession("old|1|1", hash: "samehash"));
        _repo.UpsertSession(BuildSession("new|2|2", hash: "samehash"));

        Assert.NotNull(_repo.LoadSessionByHash("samehash"));
        var newRow = _repo.LoadSessionByFastKey("new|2|2");
        Assert.NotNull(newRow);
        Assert.Null(newRow!.ContentHash);
        // 旧行未被删除（合并策略由业务层按 updated_at 决定）。
        Assert.NotNull(_repo.LoadSessionByFastKey("old|1|1"));
    }

    [Fact]
    public void TouchSessionPosition_UpdatesPositionAndRate()
    {
        _repo.UpsertSession(BuildSession("t|1|1"));
        _repo.TouchSessionPosition("t|1|1", 88000, 2.0);

        var loaded = _repo.LoadSessionByFastKey("t|1|1");
        Assert.NotNull(loaded);
        Assert.Equal(88000, loaded!.LastPositionMs);
        Assert.Equal(2.0, loaded.Rate, 3);
    }

    [Fact]
    public void DeleteSession_RemovesRowAndDetails()
    {
        _repo.UpsertSession(BuildSession("d|1|1", hash: "hashd", cutPoints: 2, notes: 1));
        _repo.DeleteSession("d|1|1");

        Assert.Null(_repo.LoadSessionByFastKey("d|1|1"));
        Assert.Null(_repo.LoadSessionByHash("hashd"));
    }
}
