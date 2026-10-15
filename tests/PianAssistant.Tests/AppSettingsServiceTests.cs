using System.IO;
using PianAssistant.Models;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

public class AppSettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public AppSettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pian-assistant-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void Defaults_ScreenshotsDirectory_UnderDataRoot()
    {
        var svc = new AppSettingsService(_tempDir);
        var expected = Path.Combine(_tempDir, "screenshots");
        Assert.Equal(expected, svc.ScreenshotsDirectory);
    }

    [Fact]
    public void Defaults_ClipsDirectory_UnderDataRoot()
    {
        var svc = new AppSettingsService(_tempDir);
        var expected = Path.Combine(_tempDir, "clips");
        Assert.Equal(expected, svc.ClipsDirectory);
    }

    [Fact]
    public void Defaults_SnapshotFormat_IsPng()
    {
        var svc = new AppSettingsService(_tempDir);
        Assert.Equal("png", svc.SnapshotFormat);
    }

    [Fact]
    public void AddRecentFile_InsertsAtFront()
    {
        var svc = new AppSettingsService(_tempDir);
        svc.AddRecentFile("a.mp4");
        svc.AddRecentFile("b.mp4");
        Assert.Equal("b.mp4", svc.RecentFiles[0]);
        Assert.Equal("a.mp4", svc.RecentFiles[1]);
    }

    [Fact]
    public void AddRecentFile_Deduplicates()
    {
        var svc = new AppSettingsService(_tempDir);
        svc.AddRecentFile("a.mp4");
        svc.AddRecentFile("b.mp4");
        svc.AddRecentFile("a.mp4"); // re-add → moves to front, not duplicate
        Assert.Equal(2, svc.RecentFiles.Count);
        Assert.Equal("a.mp4", svc.RecentFiles[0]);
    }

    [Fact]
    public void AddRecentFile_CapsAt10()
    {
        var svc = new AppSettingsService(_tempDir);
        for (int i = 0; i < 15; i++)
            svc.AddRecentFile($"file{i}.mp4");
        Assert.Equal(10, svc.RecentFiles.Count);
        Assert.Equal("file14.mp4", svc.RecentFiles[0]);
    }

    [Fact]
    public void SetHotkeyOverride_PersistsAcrossInstances()
    {
        var svc1 = new AppSettingsService(_tempDir);
        svc1.SetHotkeyOverride(HotkeyAction.PlayPause, "Ctrl+P");

        var svc2 = new AppSettingsService(_tempDir);
        var overrides = svc2.GetHotkeyOverrides();
        Assert.True(overrides.ContainsKey(HotkeyAction.PlayPause));
        Assert.Equal("Ctrl+P", overrides[HotkeyAction.PlayPause]);
    }

    [Fact]
    public void ClearHotkeyOverride_RemovesOverride()
    {
        var svc = new AppSettingsService(_tempDir);
        svc.SetHotkeyOverride(HotkeyAction.PlayPause, "Ctrl+P");
        svc.ClearHotkeyOverride(HotkeyAction.PlayPause);
        Assert.Empty(svc.GetHotkeyOverrides());
    }

    [Fact]
    public void SetSnapshotFormat_InvalidValue_Throws()
    {
        var svc = new AppSettingsService(_tempDir);
        Assert.Throws<ArgumentException>(() => svc.SetSnapshotFormat("gif"));
    }

    [Fact]
    public void ScreenshotsAndClipsDirectories_DerivedFromDataRoot()
    {
        var svc1 = new AppSettingsService(_tempDir);
        svc1.SetDataRoot("/my/data");

        var svc2 = new AppSettingsService(_tempDir);
        // 截图与片段目录统一从数据根派生，不再支持独立配置
        Assert.Equal(Path.Combine("/my/data", "screenshots"), svc2.ScreenshotsDirectory);
        Assert.Equal(Path.Combine("/my/data", "clips"), svc2.ClipsDirectory);
    }

    // --- 配置损坏/备份恢复（M6 韧性加固）---

    [Fact]
    public void CorruptConfig_FallsBackToDefaults_AndKeepsFile()
    {
        var configPath = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(configPath, "{ this is not valid json ");

        var svc = new AppSettingsService(_tempDir);

        Assert.Equal(5, svc.SeekStepFrames); // 回退默认值
        Assert.True(File.Exists(configPath));
        Assert.Equal("{ this is not valid json ", File.ReadAllText(configPath)); // 不覆盖用户文件
    }

    [Fact]
    public void MissingConfig_WithBackup_RestoresFromBackup()
    {
        var svc1 = new AppSettingsService(_tempDir);
        svc1.SeekStepFrames = 42; // 触发保存 → 生成主文件 + 备份快照
        var configPath = Path.Combine(_tempDir, "config.json");
        Assert.True(File.Exists(configPath));
        File.Delete(configPath);

        var svc2 = new AppSettingsService(_tempDir);

        Assert.Equal(42, svc2.SeekStepFrames);
        Assert.True(File.Exists(configPath), "应从备份恢复主配置文件");
    }

    [Fact]
    public void ValidConfig_IsLoadedAndNotOverwritten()
    {
        var configPath = Path.Combine(_tempDir, "config.json");
        var json = "{\"SeekStepFrames\":7}";
        File.WriteAllText(configPath, json);

        var svc = new AppSettingsService(_tempDir);

        Assert.Equal(7, svc.SeekStepFrames);
        Assert.Equal(json, File.ReadAllText(configPath)); // 合法配置不被改写
    }
}
