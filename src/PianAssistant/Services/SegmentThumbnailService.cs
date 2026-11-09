using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PianAssistant.Services;

/// <summary>
/// 片段缩略图服务：用 ffmpeg 抽取段起始帧，缓存到
/// 数据根目录 <c>clips/thumbs/{片名}_{源路径哈希}_{startMs}.jpg</c>。
/// 已存在缓存直接命中，缺失后台异步补抽；裁切点变化重建分段时
/// 增量补抽，孤儿缓存按当前段集合清理。
/// 抽帧委托可注入（测试无需真实 ffmpeg）。
/// </summary>
public sealed class SegmentThumbnailService
{
    private readonly string _thumbsDir;
    private readonly Func<string, TimeSpan, string, Task<string>> _captureAsync;
    // 同时最多两路抽帧：切片时一次重建可能产生几十个分段，
    // 无节制并发会让 ffmpeg 挤占磁盘/CPU，UI 出现明显卡顿（v1.15.8）
    private readonly SemaphoreSlim _captureGate = new(2, 2);

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

    /// <summary>
    /// 缩略图缓存路径：thumbs/{源视频键}_{startMs}.jpg。
    /// 键中带源视频身份，避免不同视频同一毫秒位置共用同一张缓存图（v1.15.8：
    /// 旧实现只按 startMs 命名，切片后笔记列表会显示上一个视频的帧）。
    /// </summary>
    public string GetThumbnailPath(string sourcePath, TimeSpan start)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        return Path.Combine(_thumbsDir, $"{SourceKey(sourcePath)}_{(long)start.TotalMilliseconds}.jpg");
    }

    /// <summary>
    /// 确保段起始帧缩略图存在：缓存命中直接返回路径；
    /// 否则后台抽帧。抽帧失败返回 null（不抛异常，避免影响列表重建）。
    /// </summary>
    public async Task<string?> EnsureThumbnailAsync(string sourcePath, TimeSpan start)
    {
        var path = GetThumbnailPath(sourcePath, start);
        if (File.Exists(path)) return path;
        await _captureGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(path)) return path; // 排队期间已被其它任务补齐
            Directory.CreateDirectory(_thumbsDir);
            return await _captureAsync(sourcePath, start, path).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error($"[Thumbs] 抽帧失败 {sourcePath} @{start}", ex);
            return null;
        }
        finally
        {
            _captureGate.Release();
        }
    }

    /// <summary>
    /// 清理孤儿缓存：删除当前源视频名下、毫秒数不在有效集合中的 jpg，
    /// 以及旧版本按纯毫秒数命名的历史缓存（跨视频共享，本身即错误数据）。
    /// 其它源视频的缓存保留，切回时仍可命中。返回删除数量。
    /// </summary>
    public int PruneOrphans(string sourcePath, IReadOnlyCollection<long> validStartMs)
    {
        ArgumentNullException.ThrowIfNull(validStartMs);
        if (!Directory.Exists(_thumbsDir)) return 0;
        var prefix = SourceKey(sourcePath) + "_";
        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(_thumbsDir, "*.jpg"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            // 旧格式：纯毫秒数，无法判定属于哪个视频，一律清理
            if (long.TryParse(name, out var legacyMs))
            {
                _ = legacyMs;
                TryDelete(file, ref deleted);
                continue;
            }
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var tail = name[prefix.Length..];
            if (long.TryParse(tail, out var ms) && validStartMs.Contains(ms)) continue;
            TryDelete(file, ref deleted);
        }
        return deleted;
    }

    private static void TryDelete(string file, ref int deleted)
    {
        try { File.Delete(file); deleted++; }
        catch (Exception ex) { AppLog.Error($"[Thumbs] 清理孤儿缓存失败 {file}", ex); }
    }

    /// <summary>源视频缓存键：{片名}_{完整路径 SHA-256 前 16 位}，可读且稳定。</summary>
    private static string SourceKey(string sourcePath)
    {
        var stem = OutputDirectoryResolver.Sanitize(Path.GetFileNameWithoutExtension(sourcePath)) ?? "video";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath));
        return $"{stem}_{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
    }
}
