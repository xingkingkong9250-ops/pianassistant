using System.IO;
using PianAssistant.Models;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

public class NotesExportServiceTests : IDisposable
{
    private readonly string _tempDir;

    public NotesExportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-export-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private static List<ActivityEntry> SampleActivities() => new()
    {
        new ActivityEntry(ActivityType.Note, TimeSpan.FromSeconds(83.5), "[构图] 三分法"),
        new ActivityEntry(ActivityType.Screenshot, TimeSpan.FromSeconds(90),
            "截图 00:01:30", @"C:\out\shot.png"),
        new ActivityEntry(ActivityType.Clip, TimeSpan.FromSeconds(60),
            "片段 00:01:00→00:02:00", @"C:\out\clip.mp4", TimeSpan.FromSeconds(60)),
        new ActivityEntry(ActivityType.CutPoint, TimeSpan.FromSeconds(180), "裁切点 00:03:00"),
    };

    [Fact]
    public void ExportMarkdown_ContainsTimecodeNoteHeading()
    {
        var path = Path.Combine(_tempDir, "notes.md");
        NotesExportService.ExportMarkdown(SampleActivities(), path);
        var text = File.ReadAllText(path);
        Assert.Contains("## 00:01:23.500 — 笔记", text);
        Assert.Contains("[构图] 三分法", text);
    }

    [Fact]
    public void ExportMarkdown_ContainsScreenshotAndClipPathsAsListItems()
    {
        var path = Path.Combine(_tempDir, "notes.md");
        NotesExportService.ExportMarkdown(SampleActivities(), path);
        var text = File.ReadAllText(path);
        Assert.Contains("- 📷 截图 00:01:30", text);
        Assert.Contains("shot.png", text);
        Assert.Contains("- ✂ 片段 00:01:00→00:02:00", text);
        Assert.Contains("clip.mp4", text);
    }

    [Fact]
    public void ExportText_ContainsTimecodesAndPaths()
    {
        var path = Path.Combine(_tempDir, "notes.txt");
        NotesExportService.ExportText(SampleActivities(), path);
        var text = File.ReadAllText(path);
        Assert.Contains("[00:01:23.500] 笔记：[构图] 三分法", text);
        Assert.Contains("shot.png", text);
        Assert.Contains("clip.mp4", text);
    }

    [Fact]
    public void Export_MarkdownByMdExtension_TextOtherwise()
    {
        var md = Path.Combine(_tempDir, "a.md");
        var txt = Path.Combine(_tempDir, "a.txt");
        NotesExportService.Export(SampleActivities(), md);
        NotesExportService.Export(SampleActivities(), txt);
        Assert.Contains("## 00:01:23.500 — 笔记", File.ReadAllText(md));
        Assert.DoesNotContain("## 00:01:23.500 — 笔记", File.ReadAllText(txt));
    }

    [Fact]
    public void ExportMarkdown_EmptyActivities_WritesHeaderOnly()
    {
        var path = Path.Combine(_tempDir, "empty.md");
        NotesExportService.ExportMarkdown(Array.Empty<ActivityEntry>(), path);
        var text = File.ReadAllText(path);
        Assert.Contains("# 拉片笔记", text);
    }
}
