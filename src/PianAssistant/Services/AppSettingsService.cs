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
    private readonly string _bootstrapRoot;
    private readonly string _configPath;
    private readonly AppSettings _settings;

    public event EventHandler? Changed;

    public AppSettings Root { get; }
    public AppSettings Settings => _settings;

    public AppSettingsService(string? dataRootOverride = null)
    {
        // 引导根：config.json 固定放在这里，不随数据根迁移。
        _bootstrapRoot = dataRootOverride ?? DataRootResolver.BootstrapRoot;
        Directory.CreateDirectory(_bootstrapRoot);
        _configPath = Path.Combine(_bootstrapRoot, "config.json");
        // 启动自愈：恢复上次崩溃残留的 .backup 快照（仅当主配置缺失时）。
        TryRestoreMissingConfig();
        _settings = Load();
        Root = _settings;
    }

    /// <summary>固定引导根（config.json 所在）。供配置读写使用。</summary>
    public string BootstrapRoot => _bootstrapRoot;

    /// <summary>
    /// 原子写自愈：若 config.json 缺失但存在上次写入残留的 <c>.backup</c> 快照，
    /// 则从快照恢复主文件。绝不覆盖仍存在的（可能非法的）用户配置文件。
    /// </summary>
    private void TryRestoreMissingConfig()
    {
        if (File.Exists(_configPath)) return;
        var backup = AtomicTextFile.GetBackupPath(_configPath);
        if (!File.Exists(backup)) return;
        try
        {
            AtomicTextFile.RestoreBackup(_configPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppSettings] 备份恢复失败: {ex.Message}");
        }
    }

    /// <summary>解析后的数据根（env &gt; config.DataRoot &gt; 引导根）。</summary>
    public string DataRoot => DataRootResolver.ResolveDataRoot(_settings.DataRoot, _bootstrapRoot);

    /// <summary>日志目录（数据根下 logs）。</summary>
    public string LogsDirectory => Path.Combine(DataRoot, "logs");

    /// <summary>数据库文件路径（数据根下 pianassistant.db）。</summary>
    public string DatabasePath => Path.Combine(DataRoot, "pianassistant.db");

    /// <summary>截图目录：统一从数据根派生（<c>DataRoot/screenshots</c>），不再支持独立配置。</summary>
    public string ScreenshotsDirectory => Path.Combine(DataRoot, "screenshots");

    /// <summary>片段目录：统一从数据根派生（<c>DataRoot/clips</c>），不再支持独立配置。</summary>
    public string ClipsDirectory => Path.Combine(DataRoot, "clips");

    public string SnapshotFormat =>
        string.IsNullOrWhiteSpace(_settings.SnapshotFormat)
            ? ExportPresets.DefaultSnapshotFormat
            : _settings.SnapshotFormat!;

    /// <summary>截图分辨率预设（source / 1080p / 720p / 480p），默认 1080p。</summary>
    public string SnapshotResolution =>
        ExportPresets.NormalizeResolution(_settings.SnapshotResolution);

    /// <summary>片段导出容器格式（mp4 / mkv / mov），默认 mp4。</summary>
    public string VideoExportFormat =>
        ExportPresets.NormalizeVideoFormat(_settings.VideoExportFormat);

    /// <summary>片段导出分辨率预设（source / 1080p / 720p / 480p），默认 1080p。</summary>
    public string VideoExportResolution =>
        ExportPresets.NormalizeResolution(_settings.VideoExportResolution);

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

    /// <summary>撤回步数上限（1~50，默认 5；实时生效）。</summary>
    public int MaxUndoSteps
    {
        get => Math.Clamp(_settings.MaxUndoSteps, 1, 50);
        set { _settings.MaxUndoSteps = Math.Clamp(value, 1, 50); Save(); }
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

    /// <summary>是否自动恢复上次拉片记录（素材会话）；默认开。</summary>
    public bool RestoreSession
    {
        get => _settings.RestoreSession;
        set { _settings.RestoreSession = value; Save(); }
    }

    /// <summary>恢复播放位置与倍速；默认开（仅在 RestoreSession 开启时生效）。</summary>
    public bool RestorePlaybackPosition
    {
        get => _settings.RestorePlaybackPosition;
        set { _settings.RestorePlaybackPosition = value; Save(); }
    }

    /// <summary>音量（0~100，软件级记忆：跨视频/会话保持，不写入视频数据）。</summary>
    public int Volume
    {
        get => Math.Clamp(_settings.Volume, 0, 100);
        set { _settings.Volume = Math.Clamp(value, 0, 100); Save(); }
    }

    /// <summary>开启「超 1080P 素材推荐生成代理」提示（v1.11.0，默认开）。</summary>
    public bool ProxyPromptEnabled
    {
        get => _settings.ProxyPromptEnabled;
        set { _settings.ProxyPromptEnabled = value; Save(); }
    }

    /// <summary>代理档位（720p / 540p / 360p），默认 720p。</summary>
    public string ProxyPreset =>
        Models.ProxyPreset.FromId(_settings.ProxyPreset).Id;

    /// <summary>设置代理档位（非法值回落到 720p）。</summary>
    public void SetProxyPreset(string presetId)
    {
        _settings.ProxyPreset = Models.ProxyPreset.FromId(presetId).Id;
        Save();
    }

    /// <summary>代理缓存容量上限（GB，0 表示不限制，默认 50）。</summary>
    public int ProxyCacheLimitGb
    {
        get => Math.Clamp(_settings.ProxyCacheLimitGb, 0, 2000);
        set { _settings.ProxyCacheLimitGb = Math.Clamp(value, 0, 2000); Save(); }
    }

    /// <summary>代理生成性能档位（quiet / balanced / turbo），默认 balanced（v1.12.0）。</summary>
    public string ProxyPerformance =>
        Models.ProxyPerformance.FromId(_settings.ProxyPerformance).Id;

    /// <summary>设置代理生成性能档位（非法值回落到 balanced）。</summary>
    public void SetProxyPerformance(string id)
    {
        _settings.ProxyPerformance = Models.ProxyPerformance.FromId(id).Id;
        Save();
    }

    /// <summary>
    /// 设置数据根（写回 config.json，config.json 本身仍在引导目录）。
    /// 仅切换路径、不搬迁任何文件；生效需重启。
    /// </summary>
    public void SetDataRoot(string? dataRoot)
    {
        _settings.DataRoot = string.IsNullOrWhiteSpace(dataRoot) ? null : dataRoot!.Trim();
        Save();
    }

    public void SetSnapshotFormat(string format)
    {
        if (format != "png" && format != "jpg")
            throw new ArgumentException("format must be 'png' or 'jpg'", nameof(format));
        _settings.SnapshotFormat = format;
        Save();
    }

    /// <summary>设置截图分辨率预设（source / 1080p / 720p / 480p）。</summary>
    public void SetSnapshotResolution(string resolution)
    {
        ExportPresets.EnsureValidResolution(resolution);
        _settings.SnapshotResolution = ExportPresets.NormalizeResolution(resolution);
        Save();
    }

    /// <summary>设置片段导出容器格式（mp4 / mkv / mov）。</summary>
    public void SetVideoExportFormat(string format)
    {
        ExportPresets.EnsureValidVideoFormat(format);
        _settings.VideoExportFormat = ExportPresets.NormalizeVideoFormat(format);
        Save();
    }

    /// <summary>设置片段导出分辨率预设（source / 1080p / 720p / 480p）。</summary>
    public void SetVideoExportResolution(string resolution)
    {
        ExportPresets.EnsureValidResolution(resolution);
        _settings.VideoExportResolution = ExportPresets.NormalizeResolution(resolution);
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

    /// <summary>引导根（config.json 所在）；等价于数据根解析的兜底值。</summary>
    public static string GetEffectiveRoot() => DataRootResolver.BootstrapRoot;
}

/// <summary>
/// Persisted application settings. Serialized as JSON.
/// </summary>
public sealed class AppSettings
{
    public string? ScreenshotsDirectory { get; set; }
    public string? ClipsDirectory { get; set; }
    public string? SnapshotFormat { get; set; }
    /// <summary>截图分辨率预设（source / 1080p / 720p / 480p；null 表示默认 1080p）。</summary>
    public string? SnapshotResolution { get; set; }
    /// <summary>片段导出容器格式（mp4 / mkv / mov；null 表示默认 mp4）。</summary>
    public string? VideoExportFormat { get; set; }
    /// <summary>片段导出分辨率预设（source / 1080p / 720p / 480p；null 表示默认 1080p）。</summary>
    public string? VideoExportResolution { get; set; }
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
    /// <summary>数据根（真正的数据存储位置）；null/空白表示用默认引导目录。</summary>
    public string? DataRoot { get; set; }
    /// <summary>是否自动恢复上次拉片记录（素材会话）；默认开。</summary>
    public bool RestoreSession { get; set; } = true;
    /// <summary>恢复播放位置与倍速；默认开。</summary>
    public bool RestorePlaybackPosition { get; set; } = true;
    /// <summary>音量（0~100，软件级记忆，默认 100）。</summary>
    public int Volume { get; set; } = 100;
    /// <summary>撤回步数上限（1~50，默认 5；超出丢弃最旧快照）。</summary>
    public int MaxUndoSteps { get; set; } = 5;
    /// <summary>超 1080P 素材是否提示生成代理（默认开）。</summary>
    public bool ProxyPromptEnabled { get; set; } = true;
    /// <summary>代理档位 Id（720p / 540p / 360p；null 表示默认 720p）。</summary>
    public string? ProxyPreset { get; set; }
    /// <summary>代理缓存容量上限（GB；0 表示不限制，默认 50）。</summary>
    public int ProxyCacheLimitGb { get; set; } = 50;

    /// <summary>代理生成性能档位（quiet/balanced/turbo），默认 balanced（v1.12.0）。</summary>
    public string? ProxyPerformance { get; set; }
}

[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext
{
}
