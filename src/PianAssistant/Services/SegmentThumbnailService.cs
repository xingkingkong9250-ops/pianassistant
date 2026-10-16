using System.IO;

namespace PianAssistant.Services;

/// <summary>
/// 片段缩略图服务：用 ffmpeg 抽取段起始帧，缓存到
/// 数据根目录 <c>clips/thumbs/{startMs}.jpg</c>。
/// 已存在缓存直接命中，缺失后台异步补抽；裁切点变化重建分段时
/// 增量补抽，孤儿缓存按当前段集合清理。
/// 抽帧委托可注入（测试无需真实 ffmpeg）。
/// </summary>
public sealed class SegmentThumbnailService
{
    private readonly string _thumbsDir;
    private readonly Func<string, TimeSpan, string, Task<string>> _captureAsync;

    /// <param name="clipsDirectory">片段输出根目录（thumbs 子目录自动派生）。</param>
    /// <param name="captureAsync">抽帧委托：(源视频, 位置, 输出路径) → 输出路径。</param>
    public SegmentThumbnailService(
        string clipsDirectory,
        Func<string, TimeSpan, string, Task<string>> captureAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clipsDirectory);
        ArgumentNullException.ThrowIfNull(captureAsync);
        _thumbsDir = Path.Combine(clipsDirectory, "thumbs");
        _captureAsync = captureAsync;
    }

    /// <summary>生产构造：委托走 <see cref="FfmpegRunner.CaptureSingleFrame"/>。</summary>
    public static SegmentThumbnailService Create(FfmpegRunner ffmpeg, string clipsDirectory)
    {
        ArgumentNullException.ThrowIfNull(ffmpeg);
        return new SegmentThumbnailService(clipsDirectory,
            (source, position, outputPath) => Task.Run(() =>
            {
                ffmpeg.CaptureSingleFrame(source, position, outputPath);
                return outputPath;
            }));
    }

    /// <summary>缩略图缓存路径：thumbs/{startMs}.jpg。</summary>
    public string GetThumbnailPath(TimeSpan start) =>
        Path.Combine(_thumbsDir, $"{(long)start.TotalMilliseconds}.jpg");

    /// <summary>
    /// 确保段起始帧缩略图存在：缓存命中直接返回路径；
    /// 否则后台抽帧。抽帧失败返回 null（不抛异常，避免影响列表重建）。
    /// </summary>
    public async Task<string?> EnsureThumbnailAsync(string sourcePath, TimeSpan start)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var path = GetThumbnailPath(start);
        if (File.Exists(path)) return path;
        try
        {
            Directory.CreateDirectory(_thumbsDir);
            return await _captureAsync(sourcePath, start, path);
        }
        catch (Exception ex)
        {
            AppLog.Error($"[Thumbs] 抽帧失败 {sourcePath} @{start}", ex);
            return null;
        }
    }

    /// <summary>
    /// 清理孤儿缓存：删除 thumbs 下文件名（毫秒数）不在有效集合中的 jpg。
    /// 返回删除数量。
    /// </summary>
    public int PruneOrphans(IReadOnlyCollection<long> validStartMs)
    {
        ArgumentNullException.ThrowIfNull(validStartMs);
        if (!Directory.Exists(_thumbsDir)) return 0;
        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(_thumbsDir, "*.jpg"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (long.TryParse(name, out var ms) && validStartMs.Contains(ms)) continue;
            try { File.Delete(file); deleted++; }
            catch (Exception ex) { AppLog.Error($"[Thumbs] 清理孤儿缓存失败 {file}", ex); }
        }
        return deleted;
    }
}
