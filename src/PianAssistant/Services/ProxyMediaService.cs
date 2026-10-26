using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// 代理媒体服务（v1.11.0）：探测源分辨率 → 判断是否需要代理 → 生成并复用低码率代理文件。
///
/// 缓存键为「路径 + 大小 + mtime」的短哈希（<see cref="ContentFingerprint.ComputeFastKey"/>，
/// 瞬时可得，不读取文件内容）；目录形如 <c>{数据根}\proxy\{key16}_{档位}\</c>，
/// 内含 <c>proxy.mp4</c> 与 <c>meta.json</c>（来源路径 / 时长 / 最近访问时间）。
/// 最近访问时间用于容量上限的 LRU 清理。
///
/// 首次生成优先用 h264_nvenc（速度快、CPU 占用低）；硬编码不可用时自动回退 libx264。
/// 生成过程先写 <c>proxy.part.mp4</c>，成功后再改名，避免半成品被当成有效代理。
/// </summary>
public sealed class ProxyMediaService : IDisposable
{
    private const string ProxyFileName = "proxy.mp4";
    private const string PartialFileName = "proxy.part.mp4";
    private const string MetaFileName = "meta.json";

    private readonly FfmpegRunner _ffmpeg;
    private readonly AppSettingsService _settings;
    private readonly string _proxyRoot;

    /// <summary>代理缓存根目录（数据根下 proxy）。</summary>
    public string ProxyRoot => _proxyRoot;

    public ProxyMediaService(
        FfmpegRunner ffmpeg,
        AppSettingsService settings,
        string? proxyRoot = null)
    {
        _ffmpeg = ffmpeg ?? throw new ArgumentNullException(nameof(ffmpeg));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _proxyRoot = proxyRoot ?? Path.Combine(settings.DataRoot, "proxy");
    }

    /// <summary>
    /// 探测媒体宽高/时长/码率（ffprobe）。失败返回 <see cref="MediaProbeResult.Unknown"/>，
    /// 绝不抛异常——探测失败按「无需代理」处理，不能阻断打开视频。
    /// </summary>
    public Task<MediaProbeResult> ProbeAsync(string sourcePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        return Task.Run(() => Probe(sourcePath), ct);
    }

    private MediaProbeResult Probe(string sourcePath)
    {
        var json = _ffmpeg.RunFfprobe(
            $"-v quiet -print_format json -show_streams -show_format \"{sourcePath}\"");
        if (string.IsNullOrWhiteSpace(json)) return MediaProbeResult.Unknown;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int width = 0, height = 0;
            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var s in streams.EnumerateArray())
                {
                    if (!s.TryGetProperty("codec_type", out var type) ||
                        type.GetString() != "video") continue;
                    if (s.TryGetProperty("width", out var w)) width = w.GetInt32();
                    if (s.TryGetProperty("height", out var h)) height = h.GetInt32();
                    break;
                }
            }
            double durationSec = 0;
            long bitrate = 0;
            if (root.TryGetProperty("format", out var format))
            {
                if (format.TryGetProperty("duration", out var d) &&
                    double.TryParse(d.GetString(), out var ds)) durationSec = ds;
                if (format.TryGetProperty("bit_rate", out var b) &&
                    long.TryParse(b.GetString(), out var bs)) bitrate = bs;
            }
            return new MediaProbeResult(width, height,
                TimeSpan.FromSeconds(Math.Max(0, durationSec)), bitrate);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProxyMedia] ffprobe 解析失败: {ex.Message}");
            return MediaProbeResult.Unknown;
        }
    }

    /// <summary>
    /// 是否建议为该素材生成代理：设置里开启提示，且分辨率超过 1080P。
    /// 普通 1080P 及以下片源不提示（v1.7.6 已消除 UI 线程阻塞，代理对其无收益）。
    /// </summary>
    public bool ShouldSuggest(MediaProbeResult probe) =>
        _settings.ProxyPromptEnabled && probe.IsAbove1080P;

    /// <summary>当前设置选定的代理档位。</summary>
    public ProxyPreset PreferredPreset => ProxyPreset.FromId(_settings.ProxyPreset);

    /// <summary>解析某素材在某档位下的缓存目录（不访问磁盘内容，瞬时返回）。</summary>
    public string ResolveCacheDirectory(string sourcePath, ProxyPreset? preset = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var key = ContentFingerprint.ComputeFastKey(sourcePath) ?? sourcePath;
        var hash = Sha256Hex16(key);
        return Path.Combine(_proxyRoot, $"{hash}_{(preset ?? PreferredPreset).Id}");
    }

    /// <summary>该素材在该档位下的代理文件路径（无论是否已生成）。</summary>
    public string ResolveProxyPath(string sourcePath, ProxyPreset? preset = null) =>
        Path.Combine(ResolveCacheDirectory(sourcePath, preset), ProxyFileName);

    /// <summary>
    /// 查找已存在的可用代理；命中时刷新「最近访问时间」以参与 LRU。
    /// 文件不存在 / 长度为 0（上次生成中途退出残留）均视为未命中。
    /// </summary>
    public bool TryGetExisting(string sourcePath, out string proxyPath, ProxyPreset? preset = null)
    {
        proxyPath = ResolveProxyPath(sourcePath, preset);
        try
        {
            var fi = new FileInfo(proxyPath);
            if (!fi.Exists || fi.Length == 0) return false;
            TouchMeta(Path.GetDirectoryName(proxyPath)!, sourcePath, preset ?? PreferredPreset);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProxyMedia] 查找代理失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 生成代理（阻塞调用，供上层 Task.Run 包装）。
    /// 先尝试 NVENC 硬编码，失败自动回退 x264；成功后写 meta 并返回结果。
    /// </summary>
    public ProxyGenerationResult Generate(
        string sourcePath,
        MediaProbeResult probe,
        ProxyPreset? preset,
        Action<double>? onProgress,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var actual = preset ?? PreferredPreset;
        var dir = ResolveCacheDirectory(sourcePath, actual);
        Directory.CreateDirectory(dir);
        var finalPath = Path.Combine(dir, ProxyFileName);
        var partPath = Path.Combine(dir, PartialFileName);
        var durationMs = probe.Duration.TotalMilliseconds;
        var usedHardware = true;

        try
        {
            TryGenerate(hardware: true, partPath);
        }
        catch (OperationCanceledException)
        {
            SafeDelete(partPath);
            throw;
        }
        catch (Exception ex)
        {
            // 无 NVIDIA 显卡 / 驱动不支持 NVENC / 多 GPU 冲突 → 回退 x264
            System.Diagnostics.Debug.WriteLine($"[ProxyMedia] NVENC 失败，回退 x264: {ex.Message}");
            usedHardware = false;
            SafeDelete(partPath);
            TryGenerate(hardware: false, partPath);
        }

        // 成功后才落成正式文件名，避免半成品被当成有效代理
        if (File.Exists(finalPath)) SafeDelete(finalPath);
        File.Move(partPath, finalPath);
        WriteMeta(dir, sourcePath, actual, probe);
        return new ProxyGenerationResult(finalPath, actual, usedHardware);

        void TryGenerate(bool hardware, string output)
        {
            _ffmpeg.GenerateProxy(sourcePath, output, actual, hardware, durationMs, ct, onProgress);
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
                throw new InvalidOperationException("代理文件未生成或长度为 0。");
        }
    }

    /// <summary>
    /// 按容量上限清理缓存（LRU：按 meta 的最近访问时间从旧到新删除）。
    /// 上限来自设置（GB），0 表示不限制。返回删除的目录数。
    /// </summary>
    public int EnforceCacheLimit()
    {
        var limitGb = _settings.ProxyCacheLimitGb;
        if (limitGb <= 0 || !Directory.Exists(_proxyRoot)) return 0;

        var limitBytes = limitGb * 1024L * 1024L * 1024L;
        var entries = new List<(string Dir, long Size, DateTime LastAccess)>();
        long total = 0;
        foreach (var dir in Directory.EnumerateDirectories(_proxyRoot))
        {
            try
            {
                var proxy = Path.Combine(dir, ProxyFileName);
                var fi = new FileInfo(proxy);
                if (!fi.Exists) continue;
                total += fi.Length;
                entries.Add((dir, fi.Length, ReadLastAccess(dir, fi.LastWriteTimeUtc)));
            }
            catch { /* 单个目录异常不影响整体清理 */ }
        }
        if (total <= limitBytes) return 0;

        var removed = 0;
        foreach (var entry in entries.OrderBy(e => e.LastAccess))
        {
            if (total <= limitBytes) break;
            try
            {
                Directory.Delete(entry.Dir, recursive: true);
                total -= entry.Size;
                removed++;
            }
            catch { /* 占用中/权限不足则跳过 */ }
        }
        return removed;
    }

    private static DateTime ReadLastAccess(string dir, DateTime fallbackUtc)
    {
        try
        {
            var meta = ReadMeta(dir);
            return meta?.LastAccessUtc ?? fallbackUtc;
        }
        catch { return fallbackUtc; }
    }

    private void TouchMeta(string dir, string sourcePath, ProxyPreset preset)
    {
        try
        {
            var meta = ReadMeta(dir) ?? new ProxyMeta();
            meta.SourcePath = sourcePath;
            meta.Preset = preset.Id;
            meta.LastAccessUtc = DateTime.UtcNow;
            WriteMeta(dir, meta);
        }
        catch { /* meta 仅用于 LRU，失败不影响播放 */ }
    }

    private void WriteMeta(string dir, string sourcePath, ProxyPreset preset, MediaProbeResult probe) =>
        WriteMeta(dir, new ProxyMeta
        {
            SourcePath = sourcePath,
            Preset = preset.Id,
            DurationSeconds = probe.Duration.TotalSeconds,
            SourceWidth = probe.Width,
            SourceHeight = probe.Height,
            LastAccessUtc = DateTime.UtcNow,
        });

    private void WriteMeta(string dir, ProxyMeta meta)
    {
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(meta, ProxyMetaJsonContext.Default.ProxyMeta);
        AtomicTextFile.WriteAllText(Path.Combine(dir, MetaFileName), json);
    }

    private static ProxyMeta? ReadMeta(string dir)
    {
        var path = Path.Combine(dir, MetaFileName);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<ProxyMeta>(
                File.ReadAllText(path), ProxyMetaJsonContext.Default.ProxyMeta);
        }
        catch { return null; }
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>取 SHA-256 的前 16 位十六进制（缓存目录名）。</summary>
    private static string Sha256Hex16(string text)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    public void Dispose() { /* 目前无长生命周期资源；保留以便后续持有队列 */ }
}

/// <summary>代理缓存元数据（用于 LRU 与来源追溯）。</summary>
public sealed class ProxyMeta
{
    public string? SourcePath { get; set; }
    public string? Preset { get; set; }
    public double DurationSeconds { get; set; }
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }
    public DateTime LastAccessUtc { get; set; }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(ProxyMeta))]
internal sealed partial class ProxyMetaJsonContext : JsonSerializerContext
{
}
