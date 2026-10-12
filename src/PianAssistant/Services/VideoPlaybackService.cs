using LibVLCSharp.Shared;
using System.IO;

namespace PianAssistant.Services;

/// <summary>
/// LibVLC-backed video playback service.
/// Wraps <see cref="LibVLCSharp.MediaPlayer"/> to expose the
/// <see cref="IVideoPlayback"/> contract for VM binding and testing.
/// </summary>
public sealed class VideoPlaybackService : IVideoPlayback
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private Media? _media;
    private string? _currentPath;
    private bool _disposed;

    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? EndReached;
    public event EventHandler? Playing;
    public event EventHandler? Paused;
    public event EventHandler? Stopped;

    public bool IsPlaying => _mediaPlayer.IsPlaying;
    public TimeSpan Position
    {
        get
        {
            var media = _mediaPlayer.Media;
            if (media is null || media.Duration <= 0) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds(media.Duration * _mediaPlayer.Position);
        }
    }
    public TimeSpan Length =>
        _media?.Duration > 0 ? TimeSpan.FromMilliseconds(_media.Duration) : TimeSpan.Zero;
    public double Rate
    {
        get => _mediaPlayer.Rate;
        set => _mediaPlayer.SetRate((float)value);
    }
    public double Fps { get; private set; } = 25.0;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public string? CurrentPath => _currentPath;

    /// <summary>
    /// Exposes the underlying MediaPlayer for the LibVLCSharp.WPF VideoView
    /// to attach (HostWindow / MediaPlayer property binding).
    /// </summary>
    public MediaPlayer LibVlcMediaPlayer => _mediaPlayer;

    public VideoPlaybackService()
    {
        _libVlc = new LibVLC();
        _mediaPlayer = new MediaPlayer(_libVlc)
        {
            EnableHardwareDecoding = true,
            Scale = 0f, // fit
        };
        _mediaPlayer.TimeChanged += (_, _) => PositionChanged?.Invoke(this, Position);
        _mediaPlayer.EndReached += (_, _) => EndReached?.Invoke(this, EventArgs.Empty);
        // Parse track info (fps/dimensions) once playback starts — tracks
        // are demuxed by then. Replaces ESAdded (not available in 3.8.2).
        _mediaPlayer.Playing += (_, _) =>
        {
            OnMediaParsed();
            Playing?.Invoke(this, EventArgs.Empty);
        };
        _mediaPlayer.Paused += (_, _) => Paused?.Invoke(this, EventArgs.Empty);
        _mediaPlayer.Stopped += (_, _) => Stopped?.Invoke(this, EventArgs.Empty);
    }

    private void OnMediaParsed()
    {
        // M0: dimensions/fps remain at defaults (Width=0, Height=0, Fps=25.0).
        // Track-data API surface differs across LibVLCSharp minor versions;
        // will refine in M2+ once the exact property names are confirmed.
        try
        {
            // Placeholder for future track metadata parsing.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VideoPlayback] OnMediaParsed failed: {ex.Message}");
        }
    }

    public void Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) throw new FileNotFoundException("Video file not found.", path);
        _currentPath = path;
        _media = new Media(_libVlc, new Uri(path));
        _mediaPlayer.Media = _media;
    }

    public void Play() => _mediaPlayer.Play();
    public void Pause() => _mediaPlayer.SetPause(true);
    public void TogglePlayPause()
    {
        if (_mediaPlayer.IsPlaying) _mediaPlayer.SetPause(true);
        else _mediaPlayer.Play();
    }
    public void Stop() => _mediaPlayer.Stop();
    public void NextFrame() => _mediaPlayer.NextFrame();

    public void Seek(TimeSpan position)
    {
        var lengthMs = _media?.Duration ?? 0;
        if (lengthMs <= 0) return;
        var pct = (float)Math.Clamp(position.TotalMilliseconds / lengthMs, 0, 1);
        _mediaPlayer.Position = pct;
    }

    public void SeekRelative(TimeSpan delta)
    {
        if (Fps <= 0) return;
        // Use Time API: more precise than Position for second-level jumps.
        var targetMs = _mediaPlayer.Time + (long)delta.TotalMilliseconds;
        if (targetMs < 0) targetMs = 0;
        var lengthMs = _media?.Duration ?? long.MaxValue;
        if (targetMs > lengthMs) targetMs = lengthMs;
        _mediaPlayer.Time = targetMs;
    }

    public void SeekPercent(double percent)
    {
        if (_media is null) return;
        var p = (float)Math.Clamp(percent, 0, 1);
        _mediaPlayer.Position = p;
    }

    public void SetRate(double rate)
    {
        var clamped = Math.Clamp(rate, 0.25, 4.0);
        _mediaPlayer.SetRate((float)clamped);
    }

    public string TakeSnapshot(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        // TakeSnapshot(num=0, filePath, width=0, height=0) → native resolution
        if (!_mediaPlayer.TakeSnapshot(0, outputPath, 0, 0))
            throw new InvalidOperationException("LibVLC TakeSnapshot failed.");
        return outputPath;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _mediaPlayer.Stop(); } catch { }
        _mediaPlayer.Dispose();
        _media?.Dispose();
        _libVlc.Dispose();
    }
}
