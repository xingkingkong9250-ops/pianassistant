using System.IO;
using PianAssistant.Helpers;
using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// Exposes ffmpeg.exe clip extraction operations with cancellation and progress.
/// </summary>
public sealed class ClipExtractionService
{
    private readonly FfmpegRunner _ffmpeg;
    private readonly string _outputDirectory;

    public ClipExtractionService(FfmpegRunner ffmpeg, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(ffmpeg);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        _ffmpeg = ffmpeg;
        _outputDirectory = outputDirectory;
    }

    public string OutputDirectory => _outputDirectory;

    /// <summary>
    /// Extract a clip. If <paramref name="reencode"/> is true (default false),
    /// uses libx264/aac for frame-accurate output; otherwise stream-copies
    /// for speed and zero loss.
    /// </summary>
    public string Extract(
        ClipRange range,
        bool reencode = false,
        CancellationToken cancellationToken = default,
        Action<double>? onProgress = null)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (!range.IsComplete)
            throw new InvalidOperationException("ClipRange is incomplete: out point must be set and after in.");
        Directory.CreateDirectory(_outputDirectory);

        var sourceName = Path.GetFileNameWithoutExtension(range.SourcePath);
        var inStamp = TimeCodeFormatter.ToFileNameFragment(range.InPoint);
        var outStamp = TimeCodeFormatter.ToFileNameFragment(range.OutPoint!.Value);
        var outName = $"{sourceName}_{inStamp}-{outStamp}{(reencode ? "_rec" : "")}.mp4";
        var outputPath = Path.Combine(_outputDirectory, outName);
        outputPath = MakeUnique(outputPath);

        if (reencode)
            _ffmpeg.ExtractClipReencode(range.SourcePath, range.InPoint, range.OutPoint.Value,
                outputPath, cancellationToken, onProgress);
        else
            _ffmpeg.ExtractClipStreamCopy(range.SourcePath, range.InPoint, range.OutPoint.Value,
                outputPath, cancellationToken, onProgress);

        return outputPath;
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
