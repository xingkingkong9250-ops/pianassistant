using System.IO;
using System.Text.Json;
using PianAssistant.Helpers;
using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// Captures a still frame from the player and records its metadata.
/// Uses <see cref="IVideoPlayback.TakeSnapshot"/> (LibVLC TakeSnapshot,
/// PNG default). Falls back to ffmpeg.exe when the LibVLC snapshot fails.
/// </summary>
public sealed class ScreenshotService
{
    private readonly IVideoPlayback _playback;
    private readonly FfmpegRunner _ffmpeg;
    private readonly string _outputDirectory;
    private readonly string _format; // "png" or "jpg"
    private readonly bool _organizeByDate;
    private readonly string? _projectSubfolder;

    public ScreenshotService(
        IVideoPlayback playback,
        FfmpegRunner ffmpeg,
        string outputDirectory,
        string format = "png",
        bool organizeByDate = false,
        string? projectSubfolder = null)
    {
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(ffmpeg);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (format != "png" && format != "jpg")
            throw new ArgumentException("format must be 'png' or 'jpg'", nameof(format));
        _playback = playback;
        _ffmpeg = ffmpeg;
        _outputDirectory = outputDirectory;
        _format = format;
        _organizeByDate = organizeByDate;
        _projectSubfolder = projectSubfolder;
    }

    public string OutputDirectory => _outputDirectory;
    public string Format => _format;

    /// <summary>
    /// Captures a screenshot at the current player position and returns
    /// the absolute output file path.
    /// </summary>
    public ScreenshotEntry Capture(string? label = null)
    {
        if (_playback.CurrentPath is null)
            throw new InvalidOperationException("No media is open.");
        // 按组织选项解析本次输出目录（日期/项目子目录），确保存在
        var outputDir = OutputDirectoryResolver.Resolve(
            _outputDirectory, _organizeByDate, _projectSubfolder, DateTime.Now);
        Directory.CreateDirectory(outputDir);

        var source = Path.GetFileNameWithoutExtension(_playback.CurrentPath);
        var pos = _playback.Position;
        var stamp = $"{source}_{TimeCodeFormatter.ToFileNameFragment(pos)}_{DateTime.Now:yyyyMMddHHmmss}.{_format}";
        var outputPath = Path.Combine(outputDir, stamp);
        outputPath = MakeUnique(outputPath);

        try
        {
            _playback.TakeSnapshot(outputPath);
        }
        catch (InvalidOperationException)
        {
            // Fallback to ffmpeg.exe for this exact position.
            _ffmpeg.CaptureSingleFrame(_playback.CurrentPath, pos, outputPath);
        }

        return new ScreenshotEntry(_playback.CurrentPath, outputPath, pos, DateTime.Now, label);
    }

    private static string MakeUnique(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return path + ".unique";
    }
}
