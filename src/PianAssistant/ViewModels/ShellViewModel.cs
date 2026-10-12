using System.Windows.Input;
using PianAssistant.Helpers;
using PianAssistant.Models;
using PianAssistant.Services;

namespace PianAssistant.ViewModels;

/// <summary>
/// Root ViewModel for the main window (W1). Holds all sub-VMs and
/// the <see cref="ServiceRegistry"/>. Handles file open, sidebar
/// panel switching, settings, and event wiring between sub-VMs.
/// </summary>
public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly ServiceRegistry _services;
    private readonly bool _ownsServices;
    private SidebarPanel _activePanel = SidebarPanel.Playlist;
    private bool _isFullscreen;
    private bool _disposed;

    public ServiceRegistry Services => _services;
    public PlayerViewModel Player { get; }
    public PlaylistViewModel Playlist { get; }
    public NotesViewModel Notes { get; }
    public ClipListViewModel Clips { get; }
    public ScreenshotGalleryViewModel Screenshots { get; }
    public SettingsViewModel Settings { get; }

    /// <summary>Currently visible sidebar panel (Playlist or Notes).</summary>
    public SidebarPanel ActivePanel
    {
        get => _activePanel;
        set => SetProperty(ref _activePanel, value);
    }

    public bool IsFullscreen
    {
        get => _isFullscreen;
        set => SetProperty(ref _isFullscreen, value);
    }

    // --- Commands ---

    public ICommand OpenFileCommand { get; }
    public ICommand ToggleSidebarPanelCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand ShowPlaylistCommand { get; }
    public ICommand ShowNotesCommand { get; }
    public ICommand OpenSettingsCommand { get; }

    // --- Events ---

    public event EventHandler<string>? OpenFileRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? SettingsRequested;

    public ShellViewModel(ServiceRegistry services, bool ownsServices = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _ownsServices = ownsServices;

        Player = new PlayerViewModel(services.Playback, services.Screenshots, services.Clips);
        Playlist = new PlaylistViewModel();
        // Notes needs the current playback position to timestamp new notes.
        Notes = new NotesViewModel(() => Player.Position);
        Clips = new ClipListViewModel();
        Screenshots = new ScreenshotGalleryViewModel();
        Settings = new SettingsViewModel(services.Settings, services.Hotkeys);

        // Wire Player → panel VMs.
        Player.ScreenshotCaptured += (_, entry) => Screenshots.Add(entry);
        Player.ClipExtracted += (_, e) => Clips.Add(e.Range);
        Player.SegmentExported += (_, e) => Clips.Add(e.Range);

        // Wire Playlist → Player.
        Playlist.CurrentChanged += OnPlaylistCurrentChanged;

        // Wire Notes → Player (seek to note position).
        Notes.SeekRequested += (_, ts) => Player.SeekPercent(
            Player.Length > TimeSpan.Zero
                ? ts.TotalMilliseconds / Player.Length.TotalMilliseconds
                : 0);

        // Wire Player fullscreen → Shell.
        Player.FullscreenToggleRequested += (_, _) => IsFullscreen = !IsFullscreen;
        Player.FullscreenExitRequested += (_, _) => IsFullscreen = false;

        // Commands.
        OpenFileCommand = new RelayCommand(RequestOpenFile);
        ToggleSidebarPanelCommand = new RelayCommand(
            () => ActivePanel = ActivePanel == SidebarPanel.Playlist
                ? SidebarPanel.Notes
                : SidebarPanel.Playlist);
        ShowPlaylistCommand = new RelayCommand(
            () => ActivePanel = SidebarPanel.Playlist);
        ShowNotesCommand = new RelayCommand(
            () => ActivePanel = SidebarPanel.Notes);
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty));
        OpenSettingsCommand = new RelayCommand(
            () => SettingsRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    /// Open a media file path: registers it in the playlist, makes it
    /// current, tells the player to load it, and adds it to recent files.
    /// </summary>
    public void OpenMedia(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var media = Playlist.AddPath(path);
        Playlist.Select(media);
        _services.Settings.AddRecentFile(path);
        // Auto-play after opening.
        Player.PlayPauseCommand.Execute(null);
    }

    private void RequestOpenFile()
    {
        // The view (code-behind) handles the actual dialog and calls OpenMedia.
        // Use a sentinel string to trigger the file dialog; the view intercepts.
        OpenFileRequested?.Invoke(this, string.Empty);
    }

    private void OnPlaylistCurrentChanged(object? sender, MediaFile? media)
    {
        if (media is null) return;
        try
        {
            Player.Open(media.Path);
            _services.Settings.AddRecentFile(media.Path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ShellVM] Open media failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Player.Dispose();
        if (_ownsServices)
            (_services.Playback as IDisposable)?.Dispose();
    }
}
