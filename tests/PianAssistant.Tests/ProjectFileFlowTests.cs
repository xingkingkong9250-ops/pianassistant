using System.IO;
using Microsoft.Data.Sqlite;
using PianAssistant.Models;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// 项目文件全流程测试（M6 C2/C6）：保存写库（含元数据）、打包导出、导入并在新项目中读回。
/// </summary>
public class ProjectFileFlowTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectFileFlowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-proj-flow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private (ProjectDatabase db, ProjectService svc) CreateService(string dbFile)
    {
        var db = new ProjectDatabase(Path.Combine(_tempDir, dbFile));
        db.EnsureInitialized();
        var settings = new AppSettingsService(_tempDir);
        var svc = new ProjectService(db, new ProjectRepository(db), settings);
        return (db, svc);
    }

    private static ScreenshotEntry MakeScreenshot(string outputPath) =>
        new("v.mp4", outputPath, TimeSpan.FromSeconds(2), DateTime.Now, "标签A");

    [Fact]
    public void SaveProject_WritesScreenshotsClipsNotesToDatabase()
    {
        var (db, svc) = CreateService("a.db");
        var shotFile = Path.Combine(_tempDir, "shot.png");
        File.WriteAllText(shotFile, "stub");
        var clipFile = Path.Combine(_tempDir, "clip.mp4");
        File.WriteAllText(clipFile, "stub");

        svc.ResolveSourcePath = () => "v.mp4";
        svc.ResolveScreenshots = () => new List<ScreenshotEntry> { MakeScreenshot(shotFile) };
        svc.ResolveClips = () => new List<(ClipRange range, string? outputPath)>
        {
            (new ClipRange("v.mp4", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4)), clipFile),
        };
        svc.ResolveNotes = () => new List<NoteEntry>
        {
            new("v.mp4", TimeSpan.FromSeconds(3), "笔记内容", DateTime.Now),
        };

        var id = svc.SaveProject("流程项目");
        Assert.NotNull(id);

        var repo = new ProjectRepository(db);
        Assert.Single(repo.GetScreenshots(id!));
        var clips = repo.GetClipsWithOutput(id!);
        Assert.Single(clips);
        Assert.Equal(clipFile, clips[0].outputPath);
        Assert.Single(repo.GetNotes(id!));
        Assert.Equal("标签A", repo.GetScreenshots(id!)[0].Label);
    }

    [Fact]
    public void ExportThenImport_RoundTripsProjectDataIntoNewDatabase()
    {
        var (db1, svc1) = CreateService("src.db");
        var shotFile = Path.Combine(_tempDir, "shot.png");
        File.WriteAllText(shotFile, "stub");

        svc1.ResolveSourcePath = () => "v.mp4";
        svc1.ResolveScreenshots = () => new List<ScreenshotEntry> { MakeScreenshot(shotFile) };
        svc1.ResolveClips = () => new List<(ClipRange range, string? outputPath)>
        {
            (new ClipRange("v.mp4", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4)), null),
        };
        svc1.ResolveNotes = () => new List<NoteEntry>
        {
            new("v.mp4", TimeSpan.FromSeconds(3), "笔记内容", DateTime.Now),
        };

        var srcId = svc1.SaveProject("导出项目");
        Assert.NotNull(srcId);
        var zipPath = Path.Combine(_tempDir, "out.paproj");
        svc1.ExportProject(srcId!, zipPath);
        Assert.True(File.Exists(zipPath));

        // 导入到全新的数据库，验证 zip 内数据自足可迁移。
        var (_, svc2) = CreateService("dst.db");
        var imported = svc2.ImportProject(zipPath);
        Assert.True(imported.Success);
        Assert.NotNull(imported.ProjectId);

        IReadOnlyList<ScreenshotEntry>? shots = null;
        IReadOnlyList<NoteEntry>? notes = null;
        IReadOnlyList<(ClipRange range, string? outputPath)>? clips = null;
        svc2.ApplyScreenshots = list => shots = list;
        svc2.ApplyNotes = list => notes = list;
        svc2.ApplyClips = list => clips = list;
        svc2.ApplySourcePath = _ => { };

        var loaded = svc2.LoadProject(imported.ProjectId!);

        Assert.True(loaded.Success);
        Assert.Single(shots!);
        Assert.Equal("标签A", shots![0].Label);
        Assert.Single(notes!);
        Assert.Equal("笔记内容", notes![0].Text);
        Assert.Single(clips!);
        Assert.Equal(TimeSpan.FromSeconds(1), clips![0].range.InPoint);
    }
}
