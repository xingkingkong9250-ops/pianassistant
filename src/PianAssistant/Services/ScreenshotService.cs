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

    /// <summary>截图格式：有 settings 时实时读取（设置窗保存后立即生效）。</summary>
    public string Format => _settings?.SnapshotFormat ?? _format;

    /// <summary>
    /// Captures a screenshot at the current player position (ffmpeg 单帧抽帧)
    /// and returns the screenshot entry.
    /// </summary>
    public ScreenshotEntry Capture(string? label = null) => CaptureAt(_playback.Position, label);

    /// <summary>
    /// 在指定时间码抽帧（不依赖播放器当前位置）：导入数据时按 CSV 时间码
    /// 重建缺失截图用（v1.2.0）。文件命名与 <see cref="Capture"/> 一致。
    /// </summary>
    public ScreenshotEntry CaptureAt(TimeSpan position, string? label = null)
    {
        if (_playback.CurrentPath is null)
            throw new InvalidOperationException("No media is open.");
        // 按组织选项解析本次输出目录（日期/项目子目录），确保存在
        var outputDir = OutputDirectoryResolver.Resolve(
            _outputDirectory, _organizeByDate, _projectSubfolder, DateTime.Now);
        Directory.CreateDirectory(outputDir);

        var source = Path.GetFileNameWithoutExtension(_playback.CurrentPath);
        var stamp = $"{source}_{TimeCodeFormatter.ToFileNameFragment(position)}_{DateTime.Now:yyyyMMddHHmmss}.{Format}";
        var outputPath = Path.Combine(outputDir, stamp);
        outputPath = MakeUnique(outputPath);

        // 后台 ffmpeg 抽帧：从源视频按时间点取帧（不依赖播放器状态）；
        // 分辨率预设非「源」时按长边封顶缩放（v1.4.0）。
        var scaleFilter = _settings is null
            ? null
            : ExportPresets.BuildScaleFilter(_settings.SnapshotResolution);
        _ffmpeg.CaptureSingleFrame(_playback.CurrentPath, position, outputPath, scaleFilter);

        return new ScreenshotEntry(_playback.CurrentPath, outputPath, position, DateTime.Now, label);
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
