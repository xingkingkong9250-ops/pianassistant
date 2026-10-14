using System.IO;
using Microsoft.Data.Sqlite;
using PianAssistant.Models;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// 项目加载/导入的韧性测试：损坏或缺失的输入返回友好失败结果，
/// 绝不抛异常到 UI，也不修改原始文件。
/// </summary>
public class ProjectServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppSettingsService _settings;
    private readonly ProjectDatabase _db;
    private readonly ProjectService _svc;

    public ProjectServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-proj-svc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _settings = new AppSettingsService(_tempDir);
        _db = new ProjectDatabase(Path.Combine(_tempDir, "p.db"));
        _db.EnsureInitialized();
        _svc = new ProjectService(_db, new ProjectRepository(_db), _settings);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void LoadProject_UnknownId_ReturnsFailureWithoutThrowing()
    {
        var result = _svc.LoadProject(Guid.NewGuid().ToString());

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void LoadProject_DatabaseWithoutSchema_ReturnsFailureWithoutThrowing()
    {
        // 未经 Initialize 的空库：查询缺表会抛 SqliteException，
        // 应被 LoadProject 捕获为友好失败，而非冒泡到 UI。
        var broken = new ProjectDatabase(Path.Combine(_tempDir, "broken.db"));
        var svc = new ProjectService(broken, new ProjectRepository(broken), _settings);

        var result = svc.LoadProject(Guid.NewGuid().ToString());

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ImportProject_NonexistentFile_ReturnsFailure()
    {
        var result = _svc.ImportProject(Path.Combine(_tempDir, "nope.paproj"));

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ImportProject_CorruptZip_ReturnsFailureWithoutThrowing()
    {
        var zip = Path.Combine(_tempDir, "broken.paproj");
        File.WriteAllBytes(zip, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });

        var result = _svc.ImportProject(zip);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.True(File.Exists(zip), "损坏的项目包不应被删除");
    }

    [Fact]
    public void SaveThenLoadProject_RoundTripsSessionData()
    {
        var screenshot = new ScreenshotEntry(
            "v.mp4", "shot.png", TimeSpan.FromSeconds(2), DateTime.Now, "标签");
        var note = new NoteEntry("v.mp4", TimeSpan.FromSeconds(3), "笔记内容", DateTime.Now);
        var clip = new ClipRange("v.mp4", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));

        _svc.ResolveSourcePath = () => "v.mp4";
        _svc.ResolveScreenshots = () => new List<ScreenshotEntry> { screenshot };
        _svc.ResolveClips = () => new List<(ClipRange range, string? outputPath)> { (clip, "clip.mp4") };
        _svc.ResolveNotes = () => new List<NoteEntry> { note };

        var id = _svc.SaveProject("拉片测试");
        Assert.NotNull(id);

        IReadOnlyList<ScreenshotEntry>? loadedShots = null;
        IReadOnlyList<NoteEntry>? loadedNotes = null;
        _svc.ApplyScreenshots = list => loadedShots = list;
        _svc.ApplyNotes = list => loadedNotes = list;

        var result = _svc.LoadProject(id!);

        Assert.True(result.Success);
        Assert.NotNull(loadedShots);
        Assert.Single(loadedShots!);
        Assert.Equal("标签", loadedShots![0].Label);
        Assert.NotNull(loadedNotes);
        Assert.Single(loadedNotes!);
        Assert.Equal("笔记内容", loadedNotes![0].Text);
    }
}
