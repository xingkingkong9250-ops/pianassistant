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
    /// <param name="externalSubtitles">
    /// 当前素材已加载的外挂字幕文件（v1.15.6）：随片段一起写入软字幕轨；null 表示不附加。
    /// </param>
    /// <param name="includeSubtitles">
    /// false 时不写入任何字幕（拼接导出的临时片段用：字幕要在最终拼接结果上处理）。
    /// </param>
    public string Extract(
        ClipRange range,
        bool reencode = false,
        CancellationToken cancellationToken = default,
        Action<double>? onProgress = null,
        IReadOnlyList<string>? externalSubtitles = null,
        bool includeSubtitles = true)
    {
        // 按组织选项解析本次输出目录（日期/项目子目录）
        var outputDir = OutputDirectoryResolver.Resolve(
            _outputDirectory, _organizeByDate, _projectSubfolder, DateTime.Now);
        return ExtractCore(range, outputDir, reencode, cancellationToken, onProgress,
            externalSubtitles, includeSubtitles);
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
        Action<double>? onProgress = null,
        IReadOnlyList<string>? externalSubtitles = null,
        bool includeSubtitles = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        return ExtractCore(range, targetDirectory, reencode, cancellationToken, onProgress,
            externalSubtitles, includeSubtitles);
    }

    private string ExtractCore(
        ClipRange range,
        string outputDir,
        bool reencode,
        CancellationToken cancellationToken,
        Action<double>? onProgress,
        IReadOnlyList<string>? externalSubtitles,
        bool includeSubtitles)
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

        if (includeSubtitles)
            TryAttachSubtitles(range, outputPath, externalSubtitles, cancellationToken);

        return outputPath;
    }

    /// <summary>
    /// 把字幕写入片段（v1.15.6）：内嵌字幕轨 + 已加载外挂字幕，统一先转 SRT、
    /// 按片段起止裁剪，再作为软字幕轨合并（视频/音频流复制，不损画质）。
    /// 失败不抛：片段已导出，仅缺字幕（错误写日志）。
    /// </summary>
    private void TryAttachSubtitles(
        ClipRange range,
        string clipPath,
        IReadOnlyList<string>? externalSubtitles,
        CancellationToken cancellationToken)
    {
        var duration = range.OutPoint!.Value - range.InPoint;
        if (duration <= TimeSpan.Zero) return;

        var tempRoot = Path.Combine(Path.GetTempPath(), $"pian-subs-{Guid.NewGuid():N}");
        var muxedPath = Path.Combine(
            Path.GetDirectoryName(clipPath) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(clipPath)}_sub_{Guid.NewGuid():N}{Path.GetExtension(clipPath)}");
        try
        {
            Directory.CreateDirectory(tempRoot);
            var tracks = new List<string>();

            // ① 内嵌字幕轨
            var embeddedCount = _ffmpeg.GetSubtitleStreamCount(range.SourcePath);
            for (int i = 0; i < embeddedCount; i++)
            {
                var full = Path.Combine(tempRoot, $"embedded{i}.full.srt");
                if (!_ffmpeg.TryExportSubtitleStreamToSrt(
                        range.SourcePath, i, full, cancellationToken))
                    continue;
                if (TrimSubtitle(full, Path.Combine(tempRoot, $"embedded{i}.srt"),
                        range.InPoint, duration) is { } trimmed)
                    tracks.Add(trimmed);
            }

            // ② 已加载的外挂字幕（同路径去重）
            if (externalSubtitles is not null)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < externalSubtitles.Count; i++)
                {
                    var path = externalSubtitles[i];
                    if (string.IsNullOrWhiteSpace(path) || !seen.Add(path) || !File.Exists(path))
                        continue;
                    var full = Path.Combine(tempRoot, $"external{i}.full.srt");
                    if (!_ffmpeg.TryConvertSubtitleToSrt(path, full, cancellationToken))
                        continue;
                    if (TrimSubtitle(full, Path.Combine(tempRoot, $"external{i}.srt"),
                            range.InPoint, duration) is { } trimmed)
                        tracks.Add(trimmed);
                }
            }

            if (tracks.Count == 0) return;

            _ffmpeg.MuxSubtitlesIntoClip(clipPath, tracks, muxedPath, cancellationToken);
            File.Move(muxedPath, clipPath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Error("[ClipExtraction] 字幕写入片段失败（片段已导出，仅缺字幕）", ex);
            try { if (File.Exists(muxedPath)) File.Delete(muxedPath); } catch { }
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    /// <summary>按片段起止裁剪整条 SRT；片段内无字幕时返回 null。</summary>
    private static string? TrimSubtitle(
        string fullSrt, string targetSrt, TimeSpan inPoint, TimeSpan duration) =>
        SubtitleTimeline.Shift(fullSrt, targetSrt, inPoint, duration) > 0 ? targetSrt : null;

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
