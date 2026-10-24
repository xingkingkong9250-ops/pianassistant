using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using PianAssistant.Models;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// M7 素材会话服务测试：两级键还原、哈希升级、防抖落库、空会话不落库与退出 flush。
/// </summary>
public class MediaSessionServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ProjectRepository _repo;
    private readonly AppSettingsService _settings;
    private readonly MediaSessionService _svc;

    public MediaSessionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-sess-svc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var db = new ProjectDatabase(Path.Combine(_tempDir, "test.db"));
        db.EnsureInitialized();
        _repo = new ProjectRepository(db);
        _settings = new AppSettingsService(_tempDir);
        _svc = new MediaSessionService(_repo, _settings);
    }

    public void Dispose()
    {
        _svc.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string CreateMedia(string name, string content = "video-bytes")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static MediaSessionSnapshot BuildSnapshot(string mediaPath,
        double positionSeconds = 12, double rate = 1.5, int cutPoints = 2)
    {
        var snapshot = new MediaSessionSnapshot
        {
            Path = mediaPath,
            Position = TimeSpan.FromSeconds(positionSeconds),
            Rate = rate,
            InPoint = TimeSpan.FromSeconds(1),
            OutPoint = TimeSpan.FromSeconds(5),
            CutPoints = Enumerable.Range(1, cutPoints)
                .Select(i => TimeSpan.FromSeconds(i)).ToList(),
            Screenshots = new List<ScreenshotEntry>
            {
                new(mediaPath, @"C:\shots\s.png", TimeSpan.FromSeconds(3), DateTime.Now, "label"),
            },
            Notes = new List<NoteEntry>
            {
                new(mediaPath, TimeSpan.FromSeconds(4), "想法"),
            },
        };
        return snapshot;
    }

    private static MediaSessionSnapshot EmptySnapshot(string mediaPath) => new()
    {
        Path = mediaPath,
        Position = TimeSpan.Zero,
        Rate = 1.0,
        CutPoints = Array.Empty<TimeSpan>(),
        Screenshots = Array.Empty<ScreenshotEntry>(),
        Notes = Array.Empty<NoteEntry>(),
    };

    [Fact]
    public void Flush_PersistsNonEmptySession()
    {
        var media = CreateMedia("a.mp4");
        _svc.CaptureSnapshot = () => BuildSnapshot(media);
        _svc.BeginSession(new MediaFile(media));
        _svc.Flush();

        var key = ContentFingerprint.ComputeFastKey(media);
        var loaded = _repo.LoadSessionByFastKey(key!);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.CutPointsMs.Count);
        Assert.Single(loaded.Screenshots);
        Assert.Single(loaded.Notes);
    }

    [Fact]
    public void Flush_EmptySession_DoesNotPersist()
    {
        var media = CreateMedia("empty.mp4");
        _svc.CaptureSnapshot = () => EmptySnapshot(media);
        _svc.BeginSession(new MediaFile(media));
        _svc.Flush();

        Assert.Null(_repo.LoadSessionByFastKey(ContentFingerprint.ComputeFastKey(media)!));
    }

    [Fact]
    public void TryRestore_NoSession_ReturnsNotRestored()
    {
        var media = CreateMedia("norestore.mp4");
        var result = _svc.TryRestore(new MediaFile(media));
        Assert.False(result.Restored);
    }

    [Fact]
    public void TryRestore_AfterPersist_RestoresAndApplies()
    {
        var media = CreateMedia("restore.mp4");
        _svc.CaptureSnapshot = () => BuildSnapshot(media);
        _svc.BeginSession(new MediaFile(media));
        _svc.Flush();

        MediaSession? applied = null;
        _svc.ApplySession = s => applied = s;
        var result = _svc.TryRestore(new MediaFile(media));

        Assert.True(result.Restored);
        Assert.Equal(2, result.CutPointCount);
        Assert.Equal(1, result.ScreenshotCount);
        Assert.Equal(1, result.NoteCount);
        Assert.True(result.PositionRestored);
        Assert.NotNull(applied);
        Assert.Equal(12000, applied!.LastPositionMs);
    }

    [Fact]
    public void TryRestore_PlaybackPositionDisabled_ZeroesPositionAndRate()
    {
        var media = CreateMedia("nopos.mp4");
        _svc.CaptureSnapshot = () => BuildSnapshot(media, positionSeconds: 12, rate: 2.0);
        _svc.BeginSession(new MediaFile(media));
        _svc.Flush();

        _settings.RestorePlaybackPosition = false;
        MediaSession? applied = null;
        _svc.ApplySession = s => applied = s;
        var result = _svc.TryRestore(new MediaFile(media));

        Assert.True(result.Restored);
        Assert.False(result.PositionRestored);
        Assert.NotNull(applied);
        Assert.Equal(0, applied!.LastPositionMs);
        Assert.Equal(1.0, applied.Rate, 3);
        // 标记类数据仍还原。
        Assert.Equal(2, applied.CutPointsMs.Count);
    }

    [Fact]
    public async Task LoadAsync_HashUpgrade_RestoresSessionFromAnotherPath()
    {
        var content = "identical-video-content";
        var pathA = CreateMedia("a.mp4", content);
        var pathB = CreateMedia("b.mp4", content);

        // 以 A 的路径持久化会话，并回写内容哈希。
        var hash = await ContentFingerprint.ComputeContentHashAsync(pathA);
        Assert.NotNull(hash);
        var session = BuildSessionModel(ContentFingerprint.ComputeFastKey(pathA)!, pathA, hash);
        _repo.UpsertSession(session);

        // 打开改名/移动后的 B：快速键未命中，但哈希命中应还原。
        MediaSession? applied = null;
        _svc.ApplySession = s => applied = s;
        var result = await _svc.LoadAsync(new MediaFile(pathB));

        Assert.True(result.Restored);
        Assert.NotNull(applied);
        Assert.Equal(2, applied!.CutPointsMs.Count);
        // B 行的哈希应被回写（同内容同哈希）。
        var rowB = _repo.LoadSessionByHash(hash!);
        Assert.NotNull(rowB);
    }

    [Fact]
    public async Task LoadAsync_HashUpgrade_NewerSessionWins()
    {
        var content = "shared-content-xyz";
        var pathA = CreateMedia("a2.mp4", content);
        var pathB = CreateMedia("b2.mp4", content);
        var hash = await ContentFingerprint.ComputeContentHashAsync(pathA);

        // B 行（旧、1 个裁切点、哈希已置空）先存在。
        var keyB = ContentFingerprint.ComputeFastKey(pathB)!;
        var rowB = BuildSessionModel(keyB, pathB, null, cutPoints: 1,
            updatedAt: DateTime.Now.AddMinutes(-10));
        _repo.UpsertSession(rowB);

        // A 行（新、2 个裁切点、持有哈希）。
        var rowA = BuildSessionModel(ContentFingerprint.ComputeFastKey(pathA)!, pathA, hash,
            cutPoints: 2, updatedAt: DateTime.Now);
        _repo.UpsertSession(rowA);

        MediaSession? applied = null;
        _svc.ApplySession = s => applied = s;
        var result = await _svc.LoadAsync(new MediaFile(pathB));

        Assert.True(result.Restored);
        Assert.Equal(2, applied!.CutPointsMs.Count); // 取较新的 A
    }

    [Fact]
    public async Task LoadAsync_SwitchMediaDuringHash_OldSessionDiscarded()
    {
        // v1.7.5 竞态：哈希计算期间切换素材，旧会话不得再次套用，
        // 旧哈希也不得回写到新素材行（否则上一个视频的笔记/截图会刷到新视频）。
        var pathA = CreateMedia("raceA.mp4", "content-A");
        var pathB = CreateMedia("raceB.mp4", "content-B");

        var hashA = await ContentFingerprint.ComputeContentHashAsync(pathA);
        var rowA = BuildSessionModel(ContentFingerprint.ComputeFastKey(pathA)!, pathA, hashA, cutPoints: 3);
        _repo.UpsertSession(rowA);

        // 注入可控哈希闸门：把 A 的哈希计算挂起，确定性制造「计算期间切走」
        var hashGate = new TaskCompletionSource<string?>();
        using var svc = new MediaSessionService(_repo, _settings,
            computeHash: (path, ct) => path == pathA
                ? hashGate.Task
                : ContentFingerprint.ComputeContentHashAsync(path, ct));
        var applied = new List<MediaSession>();
        svc.ApplySession = s => applied.Add(s);

        var loadA = svc.LoadAsync(new MediaFile(pathA)); // TryRestore 同步套用 A，哈希被闸门挡住
        Assert.Single(applied);

        svc.BeginSession(new MediaFile(pathB));          // 用户切到 B：代次 +1
        hashGate.SetResult(hashA);                        // A 的哈希此刻才算完
        await loadA;

        Assert.Single(applied); // 没有第二次套用：哈希升级结果按代次丢弃
        var rowB = _repo.LoadSessionByFastKey(ContentFingerprint.ComputeFastKey(pathB)!);
        Assert.True(rowB is null || rowB.ContentHash != hashA); // 旧哈希不得写进 B 行
    }

    [Fact]
    public async Task MarkDirty_DebouncesToSinglePersist()
    {
        var media = CreateMedia("debounce.mp4");
        _svc.DebounceInterval = TimeSpan.FromMilliseconds(40);
        _svc.CaptureSnapshot = () => BuildSnapshot(media);
        _svc.BeginSession(new MediaFile(media));

        var count = 0;
        var tcs = new TaskCompletionSource();
        _svc.Persisted += (_, _) =>
        {
            if (Interlocked.Increment(ref count) == 1) tcs.TrySetResult();
        };

        _svc.MarkDirty();
        _svc.MarkDirty();
        _svc.MarkDirty();

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        // 防抖窗口内多次标记只落库一次（单次触发后不再重排）。
        Assert.Equal(1, count);
    }

    [Fact]
    public void EndSession_FlushesThenStopsTracking()
    {
        var media = CreateMedia("end.mp4");
        _svc.CaptureSnapshot = () => BuildSnapshot(media);
        _svc.BeginSession(new MediaFile(media));

        var count = 0;
        _svc.Persisted += (_, _) => Interlocked.Increment(ref count);

        _svc.MarkDirty();
        _svc.EndSession(); // 应立即 flush 并清除跟踪状态
        Assert.Equal(1, count);

        // 跟踪已清除：后续标记与 flush 不再落库。
        _svc.MarkDirty();
        _svc.Flush();
        Assert.Equal(1, count);
    }

    [Fact]
    public void MarkDirty_BeforeBeginSession_DoesNothing()
    {
        var count = 0;
        _svc.Persisted += (_, _) => Interlocked.Increment(ref count);
        _svc.CaptureSnapshot = () => BuildSnapshot(CreateMedia("noop.mp4"));
        _svc.MarkDirty();
        _svc.Flush();
        Assert.Equal(0, count);
    }

    private static MediaSession BuildSessionModel(string fastKey, string path, string? hash,
        int cutPoints = 2, DateTime? updatedAt = null)
    {
        var session = new MediaSession
        {
            FastKey = fastKey,
            ContentHash = hash,
            Path = path,
            LastPositionMs = 12000,
            Rate = 1.5,
            UpdatedAt = updatedAt ?? DateTime.Now,
        };
        for (int i = 0; i < cutPoints; i++)
            session.CutPointsMs.Add(1000 * (i + 1));
        session.Notes.Add(new SessionNote { PositionMs = 500, Text = "n", CreatedAt = DateTime.Now });
        return session;
    }
}
