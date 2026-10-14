using System.Collections.ObjectModel;
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
    private string _screenshotsDirectory;
    private string _clipsDirectory;
    private string _snapshotFormat;
    private HotkeyAction _selectedAction;
    private string _editingGesture = string.Empty;
    private bool _globalHotkeysEnabled;
    private bool _organizeByDate;
    private string? _projectSubfolderName;
    private double _maxRate;
    private int _seekStepFrames;
    private double _rateStep;

    public SettingsViewModel(AppSettingsService settings, HotkeyService hotkeys)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(hotkeys);
        _settings = settings;
        _hotkeys = hotkeys;

        _screenshotsDirectory = settings.ScreenshotsDirectory;
        _clipsDirectory = settings.ClipsDirectory;
        _snapshotFormat = settings.SnapshotFormat;
        _selectedAction = HotkeyAction.PlayPause;
        _globalHotkeysEnabled = settings.GlobalHotkeysEnabled;
        _organizeByDate = settings.OrganizeByDate;
        _projectSubfolderName = settings.ProjectSubfolderName;
        _maxRate = settings.MaxRate;
        _seekStepFrames = settings.SeekStepFrames;
        _rateStep = settings.RateStep;

        BrowseScreenshotsCommand = new RelayCommand(BrowseScreenshots);
        BrowseClipsCommand = new RelayCommand(BrowseClips);
        SaveCommand = new RelayCommand(Save);
        ResetHotkeyCommand = new RelayCommand(ResetSelectedHotkey);
        ResetAllHotkeysCommand = new RelayCommand(_hotkeys.ResetAll);

        // Build the action list for the hotkey editor ListBox.
        foreach (HotkeyAction action in Enum.GetValues(typeof(HotkeyAction)))
            HotkeyEntries.Add(new HotkeyEntry(action, _hotkeys.Active[action]));
    }

    public string ScreenshotsDirectory
    {
        get => _screenshotsDirectory;
        set => SetProperty(ref _screenshotsDirectory, value);
    }

    public string ClipsDirectory
    {
        get => _clipsDirectory;
        set => SetProperty(ref _clipsDirectory, value);
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

    public ICommand BrowseScreenshotsCommand { get; }
    public ICommand BrowseClipsCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ResetHotkeyCommand { get; }
    public ICommand ResetAllHotkeysCommand { get; }

    public event EventHandler? BrowseScreenshotsRequested;
    public event EventHandler? BrowseClipsRequested;
    public event EventHandler? Saved;
    public event EventHandler? CloseRequested;

    private void BrowseScreenshots() => BrowseScreenshotsRequested?.Invoke(this, EventArgs.Empty);
    private void BrowseClips() => BrowseClipsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Called by the view after a folder dialog returns a path.</summary>
    public void SetScreenshotsDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ScreenshotsDirectory = path;
    }

    public void SetClipsDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ClipsDirectory = path;
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
        _settings.SetOutputDirectories(_screenshotsDirectory, _clipsDirectory);
        _settings.SetSnapshotFormat(_snapshotFormat);
        _settings.GlobalHotkeysEnabled = _globalHotkeysEnabled;
        _settings.SetOrganizationOptions(_organizeByDate, _projectSubfolderName);
        _settings.MaxRate = MaxRate;
        _settings.SeekStepFrames = SeekStepFrames;
        _settings.RateStep = RateStep;
        Saved?.Invoke(this, EventArgs.Empty);
        CloseRequested?.Invoke(this, EventArgs.Empty);
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
