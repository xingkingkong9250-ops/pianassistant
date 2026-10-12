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
    private readonly bool _organizeByDate;
    private readonly string? _projectSubfolder;

    public ClipExtractionService(FfmpegRunner ffmpeg, string outputDirectory,
        bool organizeByDate = false, string? projectSubfolder = null)
    {
        ArgumentNullException.ThrowIfNull(ffmpeg);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        _ffmpeg = ffmpeg;
        _outputDirectory = outputDirectory;
        _organizeByDate = organizeByDate;
        _projectSubfolder = projectSubfolder;
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
        // 按组织选项解析本次输出目录（日期/项目子目录），确保存在
        var outputDir = OutputDirectoryResolver.Resolve(
            _outputDirectory, _organizeByDate, _projectSubfolder, DateTime.Now);
        Directory.CreateDirectory(outputDir);

        var sourceName = Path.GetFileNameWithoutExtension(range.SourcePath);
        var inStamp = TimeCodeFormatter.ToFileNameFragment(range.InPoint);
        var outStamp = TimeCodeFormatter.ToFileNameFragment(range.OutPoint!.Value);
        var outName = $"{sourceName}_{inStamp}-{outStamp}{(reencode ? "_rec" : "")}.mp4";
        var outputPath = Path.Combine(outputDir, outName);
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
