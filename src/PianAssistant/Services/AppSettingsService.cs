using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// User-configurable application settings, persisted as
/// <c>%APPDATA%\Qianlan_App\PianAssistant\config.json</c> via
/// <see cref="AtomicTextFile"/>. DPAPI not used in M0 (no secrets).
/// </summary>
public sealed class AppSettingsService
{
    private static readonly string DefaultRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Qianlan_App", "PianAssistant");

    private readonly string _configPath;
    private readonly AppSettings _settings;

    public event EventHandler? Changed;

    public AppSettings Root { get; }
    public AppSettings Settings => _settings;

    public AppSettingsService(string? dataRootOverride = null)
    {
        var root = dataRootOverride ?? GetEffectiveRoot();
        Directory.CreateDirectory(root);
        _configPath = Path.Combine(root, "config.json");
        _settings = Load();
        Root = _settings;
    }

    public string DataRoot => Path.GetDirectoryName(_configPath)!;

    public string ScreenshotsDirectory =>
        string.IsNullOrWhiteSpace(_settings.ScreenshotsDirectory)
            ? Path.Combine(DataRoot, "screenshots")
            : _settings.ScreenshotsDirectory!;

    public string ClipsDirectory =>
        string.IsNullOrWhiteSpace(_settings.ClipsDirectory)
            ? Path.Combine(DataRoot, "clips")
            : _settings.ClipsDirectory!;

    public string SnapshotFormat =>
        string.IsNullOrWhiteSpace(_settings.SnapshotFormat)
            ? "png"
            : _settings.SnapshotFormat!;

    /// <summary>方向键左右移动帧数（1~600）。</summary>
    public int SeekStepFrames
    {
        get => Math.Clamp(_settings.SeekStepFrames, 1, 600);
        set { _settings.SeekStepFrames = Math.Clamp(value, 1, 600); Save(); }
    }

    /// <summary>方向键上下倍速步长（0.05~1.0）。</summary>
    public double RateStep
    {
        get => Math.Clamp(_settings.RateStep, 0.05, 1.0);
        set { _settings.RateStep = Math.Clamp(value, 0.05, 1.0); Save(); }
    }

    /// <summary>最高播放倍率上限（2~32）。</summary>
    public double MaxRate
    {
        get => Math.Clamp(_settings.MaxRate, 2.0, 32.0);
        set { _settings.MaxRate = Math.Clamp(value, 2.0, 32.0); Save(); }
    }

    /// <summary>全局热键开关（启用时应用失焦状态下按 S 也可截图）。</summary>
    public bool GlobalHotkeysEnabled
    {
        get => _settings.GlobalHotkeysEnabled;
        set { _settings.GlobalHotkeysEnabled = value; Save(); }
    }

    /// <summary>截图/片段是否按日期（yyyyMMdd）分子目录。</summary>
    public bool OrganizeByDate => _settings.OrganizeByDate;

    /// <summary>项目名子目录；null/空白表示未启用。</summary>
    public string? ProjectSubfolderName => _settings.ProjectSubfolderName;

    /// <summary>设置输出目录组织选项（按日期分目录 + 项目名子目录）。</summary>
    public void SetOrganizationOptions(bool byDate, string? projectName)
    {
        _settings.OrganizeByDate = byDate;
        _settings.ProjectSubfolderName = string.IsNullOrWhiteSpace(projectName)
            ? null
            : projectName.Trim();
        Save();
    }

    public IReadOnlyList<string> RecentFiles => _settings.RecentFiles;

    public void AddRecentFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _settings.RecentFiles.Remove(path);
        _settings.RecentFiles.Insert(0, path);
        if (_settings.RecentFiles.Count > 10)
            _settings.RecentFiles.RemoveRange(10, _settings.RecentFiles.Count - 10);
        Save();
    }

    public void SetOutputDirectories(string? screenshots, string? clips)
    {
        _settings.ScreenshotsDirectory = screenshots;
        _settings.ClipsDirectory = clips;
        Save();
    }

    public void SetSnapshotFormat(string format)
    {
        if (format != "png" && format != "jpg")
            throw new ArgumentException("format must be 'png' or 'jpg'", nameof(format));
        _settings.SnapshotFormat = format;
        Save();
    }

    public void SetHotkeyOverride(HotkeyAction action, string gesture)
    {
        _settings.HotkeyOverrides[action] = gesture;
        Save();
    }

    public IReadOnlyDictionary<HotkeyAction, string> GetHotkeyOverrides() =>
        _settings.HotkeyOverrides;

    public void ClearHotkeyOverride(HotkeyAction action)
    {
        if (_settings.HotkeyOverrides.Remove(action)) Save();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_configPath))
                return JsonSerializer.Deserialize<AppSettings>(
                    AtomicTextFile.ReadAllTextOrRecover(_configPath)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppSettings] Load failed: {ex.Message}");
        }
        return new AppSettings();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_settings, AppSettingsJsonContext.Default.AppSettings);
        AtomicTextFile.WriteAllText(_configPath, json);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static string GetEffectiveRoot()
    {
        var envOverride = Environment.GetEnvironmentVariable("PIAN_ASSISTANT_DATA_ROOT");
        return string.IsNullOrWhiteSpace(envOverride) ? DefaultRoot : envOverride!;
    }
}

/// <summary>
/// Persisted application settings. Serialized as JSON.
/// </summary>
public sealed class AppSettings
{
    public string? ScreenshotsDirectory { get; set; }
    public string? ClipsDirectory { get; set; }
    public string? SnapshotFormat { get; set; }
    public List<string> RecentFiles { get; set; } = new();
    public Dictionary<HotkeyAction, string> HotkeyOverrides { get; set; } = new();
    /// <summary>方向键左右每次移动帧数（1~600，默认5）。</summary>
    public int SeekStepFrames { get; set; } = 5;
    /// <summary>方向键上下每次倍速变化量（0.05~1.0，默认0.25）。</summary>
    public double RateStep { get; set; } = 0.25;
    /// <summary>最高播放倍率上限（2~32，默认8）。</summary>
    public double MaxRate { get; set; } = 8.0;
    /// <summary>是否启用全局热键（应用失焦时 S 键截图；默认关闭）。</summary>
    public bool GlobalHotkeysEnabled { get; set; } = false;
    /// <summary>输出是否按日期（yyyyMMdd）分子目录（默认关闭）。</summary>
    public bool OrganizeByDate { get; set; } = false;
    /// <summary>项目名子目录（非空时在日期层下再建一层；默认 null）。</summary>
    public string? ProjectSubfolderName { get; set; }
}

[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext
{
}
