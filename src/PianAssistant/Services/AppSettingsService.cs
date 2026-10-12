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
}

[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext
{
}
