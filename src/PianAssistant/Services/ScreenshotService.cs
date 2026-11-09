using System.IO;
using System.Text.Json;
using PianAssistant.Helpers;
using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// Captures a still frame from the source video and records its metadata.
/// v0.20.0 起不再使用播放器截屏（LibVLC TakeSnapshot 已移除）：
/// 一律由 ffmpeg 从源视频抽帧（后台执行、不干扰播放，画质为原始分辨率），
/// 由调用方 Task.Run 包装为异步。
/// </summary>
public sealed class ScreenshotService
{
    private readonly IVideoPlayback _playback;
    private readonly FfmpegRunner _ffmpeg;
    private readonly string _outputDirectory;
    private readonly string _format; // 构造时的回退格式（无 settings 时使用）
    private readonly bool _organizeByDate;
    private readonly string? _projectSubfolder;
    private readonly AppSettingsService? _settings;

    public ScreenshotService(
        IVideoPlayback playback,
        FfmpegRunner ffmpeg,
        string outputDirectory,
        string format = "png",
        bool organizeByDate = false,
        string? projectSubfolder = null,
        AppSettingsService? settings = null)
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
        _settings = settings;
    }

    public string OutputDirectory => _outputDirectory;

    /// <summary>
    /// 原片路径提供者（v1.11.0 代理媒体）：切到代理播放后 <see cref="IVideoPlayback.CurrentPath"/>
    /// 会变成代理路径，截图必须仍从源文件抽帧（原始分辨率）。未设置时退回播放器当前路径。
    /// </summary>
    public Func<string?>? SourcePathProvider { get; set; }

    /// <summary>实际用于抽帧/记录的源文件路径。</summary>
    private string? ActiveSourcePath => SourcePathProvider?.Invoke() ?? _playback.CurrentPath;

    /// <summary>截图格式：有 settings 时实时读取（设置窗保存后立即生效）。</summary>
    public string Format => _settings?.SnapshotFormat ?? _format;

    /// <summary>
    /// Captures a screenshot at the current player position (ffmpeg 单帧抽帧)
    /// and returns the screenshot entry.
    /// </summary>
    public ScreenshotEntry Capture(string? label = null) => CaptureAt(_playback.GetPrecisePosition(), label);

    /// <summary>
    /// 在指定时间码抽帧（不依赖播放器当前位置）：导入数据时按 CSV 时间码
    /// 重建缺失截图用（v1.2.0）。文件命名与 <see cref="Capture"/> 一致。
    /// <paramref name="sourceOverride"/> 用于调用方在切片前锁定源文件，
    /// 避免后台抽帧开始时已切到新视频而写错目录（v1.15.8）。
    /// </summary>
    public ScreenshotEntry CaptureAt(TimeSpan position, string? label = null, string? sourceOverride = null)
    {
        var source = sourceOverride ?? ActiveSourcePath;
        if (source is null)
            throw new InvalidOperationException("No media is open.");
        // 按组织选项解析本次输出目录（日期/项目子目录），确保存在
        var outputDir = OutputDirectoryResolver.Resolve(
            _outputDirectory, _organizeByDate, _projectSubfolder, DateTime.Now);
        // v1.13.0：截图按视频独立子文件夹存储（{片名}_{fastkey短哈希16}），
        // 避免不同视频截图混放同目录，杜绝切换素材时读取/清理互相串扰
        outputDir = Path.Combine(outputDir, PerSourceSubfolder(source));
        Directory.CreateDirectory(outputDir);

        var stem = Path.GetFileNameWithoutExtension(source);
        var stamp = $"{stem}_{TimeCodeFormatter.ToFileNameFragment(position)}_{DateTime.Now:yyyyMMddHHmmss}.{Format}";
        var outputPath = Path.Combine(outputDir, stamp);
        outputPath = MakeUnique(outputPath);

        // 后台 ffmpeg 抽帧：从源视频按时间点取帧（不依赖播放器状态）；
        // 分辨率预设非「源」时按长边封顶缩放（v1.4.0）。
        var scaleFilter = _settings is null
            ? null
            : ExportPresets.BuildScaleFilter(_settings.SnapshotResolution);
        _ffmpeg.CaptureSingleFrame(source, position, outputPath, scaleFilter);

        return new ScreenshotEntry(source, outputPath, position, DateTime.Now, label);
    }

    /// <summary>按源视频生成独立子文件夹名：{片名}_{fastkey 短哈希16}（v1.13.0）。</summary>
    private static string PerSourceSubfolder(string sourcePath)
    {
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        // 片名可能含目录非法字符（理论极少），统一过一遍清洗
        var safeStem = OutputDirectoryResolver.Sanitize(stem) ?? "video";
        var key = ContentFingerprint.ComputeFastKey(sourcePath) ?? sourcePath;
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(key));
        var hash16 = Convert.ToHexString(bytes)[..16].ToLowerInvariant();
        return $"{safeStem}_{hash16}";
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
