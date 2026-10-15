using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using PianAssistant.Helpers;
using PianAssistant.Models;
using PianAssistant.Services;

namespace PianAssistant.ViewModels;

/// <summary>
/// View model for the W3 Settings window. Binds output directories,
/// snapshot format, and hotkey overrides to the AppSettingsService.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsService _settings;
    private readonly HotkeyService _hotkeys;
    private string _snapshotFormat;
    private HotkeyAction _selectedAction;
    private string _editingGesture = string.Empty;
    private bool _globalHotkeysEnabled;
    private bool _organizeByDate;
    private string? _projectSubfolderName;
    private double _maxRate;
    private int _seekStepFrames;
    private double _rateStep;
    private string _dataRoot;
    private readonly string _originalDataRoot;
    private bool _restoreSession;
    private bool _restorePlaybackPosition;

    public SettingsViewModel(AppSettingsService settings, HotkeyService hotkeys)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(hotkeys);
        _settings = settings;
        _hotkeys = hotkeys;

        _dataRoot = settings.DataRoot;
        _originalDataRoot = settings.DataRoot;
        _restoreSession = settings.RestoreSession;
        _restorePlaybackPosition = settings.RestorePlaybackPosition;
        _snapshotFormat = settings.SnapshotFormat;
        _selectedAction = HotkeyAction.PlayPause;
        _globalHotkeysEnabled = settings.GlobalHotkeysEnabled;
        _organizeByDate = settings.OrganizeByDate;
        _projectSubfolderName = settings.ProjectSubfolderName;
        _maxRate = settings.MaxRate;
        _seekStepFrames = settings.SeekStepFrames;
        _rateStep = settings.RateStep;

        BrowseDataRootCommand = new RelayCommand(BrowseDataRoot);
        SaveCommand = new RelayCommand(Save);
        ResetHotkeyCommand = new RelayCommand(ResetSelectedHotkey);
        ResetAllHotkeysCommand = new RelayCommand(_hotkeys.ResetAll);

        // Build the action list for the hotkey editor ListBox.
        foreach (HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))
            HotkeyEntries.Add(new HotkeyEntry(action, _hotkeys.Active[action]));
    }

    /// <summary>"png" or "jpg".</summary>
    public string SnapshotFormat
    {
        get => _snapshotFormat;
        set => SetProperty(ref _snapshotFormat, value);
    }

    /// <summary>是否启用全局热键（应用失焦时按 S 截图）。</summary>
    public bool GlobalHotkeysEnabled
    {
        get => _globalHotkeysEnabled;
        set => SetProperty(ref _globalHotkeysEnabled, value);
    }

    /// <summary>输出是否按日期（yyyyMMdd）分子目录。</summary>
    public bool OrganizeByDate
    {
        get => _organizeByDate;
        set => SetProperty(ref _organizeByDate, value);
    }

    /// <summary>项目名子目录（空视为不启用）。</summary>
    public string? ProjectSubfolderName
    {
        get => _projectSubfolderName;
        set => SetProperty(ref _projectSubfolderName, value);
    }

    /// <summary>最高播放倍率上限（2~32，默认8）。</summary>
    public double MaxRate
    {
        get => _maxRate;
        set => SetProperty(ref _maxRate, Math.Clamp(value, 2.0, 32.0));
    }

    /// <summary>最高倍率预设档位（设置窗下拉框）。</summary>
    public IReadOnlyList<double> MaxRateOptions { get; } =
        new[] { 2.0, 4.0, 8.0, 12.0, 16.0, 20.0, 32.0 };

    /// <summary>方向键左右移动帧数（1~600，默认 5）。</summary>
    public int SeekStepFrames
    {
        get => _seekStepFrames;
        set => SetProperty(ref _seekStepFrames, Math.Clamp(value, 1, 600));
    }

    /// <summary>方向键上下倍速步长（0.05~1.0，默认 0.25）。</summary>
    public double RateStep
    {
        get => _rateStep;
        set => SetProperty(ref _rateStep, Math.Clamp(value, 0.05, 1.0));
    }

    /// <summary>数据路径（数据根）；修改后需重启生效，不搬迁任何文件。</summary>
    public string DataRoot
    {
        get => _dataRoot;
        set => SetProperty(ref _dataRoot, value);
    }

    /// <summary>是否自动恢复上次拉片记录（素材会话）。</summary>
    public bool RestoreSession
    {
        get => _restoreSession;
        set => SetProperty(ref _restoreSession, value);
    }

    /// <summary>是否恢复播放位置与倍速。</summary>
    public bool RestorePlaybackPosition
    {
        get => _restorePlaybackPosition;
        set => SetProperty(ref _restorePlaybackPosition, value);
    }

    /// <summary>Currently selected hotkey action for inline editing.</summary>
    public HotkeyAction SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (SetProperty(ref _selectedAction, value))
            {
                EditingGesture = _hotkeys.Active[value];
            }
        }
    }

    /// <summary>The gesture text being edited for the selected action.</summary>
    public string EditingGesture
    {
        get => _editingGesture;
        set => SetProperty(ref _editingGesture, value);
    }

    /// <summary>All hotkey entries for the settings ListBox.</summary>
    public ObservableCollection<HotkeyEntry> HotkeyEntries { get; } = new();

    public ICommand BrowseDataRootCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ResetHotkeyCommand { get; }
    public ICommand ResetAllHotkeysCommand { get; }

    public event EventHandler? BrowseDataRootRequested;
    public event EventHandler? Saved;
    public event EventHandler? CloseRequested;
    /// <summary>数据路径发生实际变化（需重启生效）时触发，由 View 层弹提示。</summary>
    public event EventHandler? DataRootChanged;
    /// <summary>校验失败（非法字符 / 目录不可写）时触发，参数为错误消息。</summary>
    public event EventHandler<string>? ValidationFailed;

    private void BrowseDataRoot() => BrowseDataRootRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Called by the view after a folder dialog returns a path (数据路径).</summary>
    public void SetDataRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        DataRoot = path;
    }

    /// <summary>
    /// 校验数据路径：空白视为「使用默认」，合法；否则检查非法字符与目录可写性。
    /// 返回 null 表示合法，否则返回错误消息。
    /// </summary>
    public string? ValidateDataRoot()
    {
        if (string.IsNullOrWhiteSpace(_dataRoot)) return null;
        try
        {
            foreach (var c in Path.GetInvalidPathChars())
                if (_dataRoot.Contains(c)) return "数据路径包含非法字符。";
            var full = Path.GetFullPath(_dataRoot.Trim());
            Directory.CreateDirectory(full);
            // 写探针验证可写性（成功后立即删除，不残留）。
            var probe = Path.Combine(full, ".write_probe_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            return null;
        }
        catch (Exception ex)
        {
            return "数据路径不可用：" + ex.Message;
        }
    }

    /// <summary>Commit the currently edited gesture for the selected action.</summary>
    public void CommitHotkey()
    {
        if (string.IsNullOrWhiteSpace(_editingGesture)) return;
        if (!HotkeyService.TryParseKeyModifiers(_editingGesture, out _, out _))
            return; // invalid gesture; keep old binding
        _hotkeys.SetCustom(_selectedAction, _editingGesture);
        _settings.SetHotkeyOverride(_selectedAction, _editingGesture);
        UpdateEntry(_selectedAction, _editingGesture);
    }

    private void ResetSelectedHotkey()
    {
        _hotkeys.ResetToDefault(_selectedAction);
        _settings.ClearHotkeyOverride(_selectedAction);
        var gesture = HotkeyService.Defaults[_selectedAction];
        EditingGesture = gesture;
        UpdateEntry(_selectedAction, gesture);
    }

    private void UpdateEntry(HotkeyAction action, string gesture)
    {
        for (int i = 0; i < HotkeyEntries.Count; i++)
        {
            if (HotkeyEntries[i].Action == action)
            {
                HotkeyEntries[i] = new HotkeyEntry(action, gesture);
                break;
            }
        }
    }

    private void Save()
    {
        // 数据路径非法/不可写时阻断保存，交由 View 层提示。
        var error = ValidateDataRoot();
        if (error is not null)
        {
            ValidationFailed?.Invoke(this, error);
            return;
        }

        var newDataRoot = string.IsNullOrWhiteSpace(_dataRoot) ? null : _dataRoot.Trim();
        var dataRootChanged = !string.Equals(
            NormalizePath(_originalDataRoot), NormalizePath(newDataRoot),
            StringComparison.OrdinalIgnoreCase);

        _settings.SetSnapshotFormat(_snapshotFormat);
        _settings.GlobalHotkeysEnabled = _globalHotkeysEnabled;
        _settings.SetOrganizationOptions(_organizeByDate, _projectSubfolderName);
        _settings.MaxRate = MaxRate;
        _settings.SeekStepFrames = SeekStepFrames;
        _settings.RateStep = RateStep;
        _settings.RestoreSession = _restoreSession;
        _settings.RestorePlaybackPosition = _restorePlaybackPosition;
        if (dataRootChanged)
            _settings.SetDataRoot(newDataRoot);

        Saved?.Invoke(this, EventArgs.Empty);
        // 仅在实际变化时提示「需重启生效」，且不搬迁任何文件。
        if (dataRootChanged)
            DataRootChanged?.Invoke(this, EventArgs.Empty);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>规范化路径用于比较（去尾部斜杠、转全路径）；失败时返回原值。</summary>
    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar); }
        catch { return path.Trim(); }
    }
}

/// <summary>Row in the hotkey editor ListBox: action + current gesture.</summary>
public sealed class HotkeyEntry
{
    public HotkeyAction Action { get; }
    public string Gesture { get; set; }

    public HotkeyEntry(HotkeyAction action, string gesture)
    {
        Action = action;
        Gesture = gesture;
    }
}
