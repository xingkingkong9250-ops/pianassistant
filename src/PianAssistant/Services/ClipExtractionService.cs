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
    private readonly AppSettingsService? _settings;

    public ClipExtractionService(FfmpegRunner ffmpeg, string outputDirectory,
        bool organizeByDate = false, string? projectSubfolder = null,
        AppSettingsService? settings = null)
    {
        ArgumentNullException.ThrowIfNull(ffmpeg);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        _ffmpeg = ffmpeg;
        _outputDirectory = outputDirectory;
        _organizeByDate = organizeByDate;
        _projectSubfolder = projectSubfolder;
        _settings = settings;
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
        // 按组织选项解析本次输出目录（日期/项目子目录）
        var outputDir = OutputDirectoryResolver.Resolve(
            _outputDirectory, _organizeByDate, _projectSubfolder, DateTime.Now);
        return ExtractCore(range, outputDir, reencode, cancellationToken, onProgress);
    }

    /// <summary>
    /// 导出到用户选定的目录（笔记面板「导出此段/导出选中」用）：
    /// 不走日期/项目子目录组织，文件名沿用自动命名规则。
    /// </summary>
    public string ExtractTo(
        ClipRange range,
        string targetDirectory,
        bool reencode = false,
        CancellationToken cancellationToken = default,
        Action<double>? onProgress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        return ExtractCore(range, targetDirectory, reencode, cancellationToken, onProgress);
    }

    private string ExtractCore(
        ClipRange range,
        string outputDir,
        bool reencode,
        CancellationToken cancellationToken,
        Action<double>? onProgress)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (!range.IsComplete)
            throw new InvalidOperationException("ClipRange is incomplete: out point must be set and after in.");
        Directory.CreateDirectory(outputDir);

        var sourceName = Path.GetFileNameWithoutExtension(range.SourcePath);
        var inStamp = TimeCodeFormatter.ToFileNameFragment(range.InPoint);
        var outStamp = TimeCodeFormatter.ToFileNameFragment(range.OutPoint!.Value);

        // 导出预设每次调用实时读取（设置窗保存后立即生效）。
        // 无 settings（测试直构）时保持旧行为：mp4 + 源分辨率（流复制）。
        var format = _settings?.VideoExportFormat ?? ExportPresets.DefaultVideoFormat;
        var scaleFilter = _settings is null
            ? null
            : ExportPresets.BuildScaleFilter(_settings.VideoExportResolution);
        // 分辨率预设非「源」时必须重编码：流复制无法改变分辨率。
        var mustReencode = reencode || scaleFilter is not null;
        var outName = $"{sourceName}_{inStamp}-{outStamp}{(mustReencode ? "_rec" : "")}.{format}";
        var outputPath = Path.Combine(outputDir, outName);
        outputPath = MakeUnique(outputPath);

        if (mustReencode)
            _ffmpeg.ExtractClipReencode(range.SourcePath, range.InPoint, range.OutPoint.Value,
                outputPath, cancellationToken, onProgress, scaleFilter);
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
