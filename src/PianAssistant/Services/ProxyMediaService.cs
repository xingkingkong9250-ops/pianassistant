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
/// v1.12.0 起支持按性能档位多段并行转码（concat 无损拼接），并按编码格式选择软/硬解
/// （H.264 软解更快，HEVC 硬解可快 2 倍以上），失败逐段回退。
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
            string? codec = null;
            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var s in streams.EnumerateArray())
                {
                    if (!s.TryGetProperty("codec_type", out var type) ||
                        type.GetString() != "video") continue;
                    if (s.TryGetProperty("width", out var w)) width = w.GetInt32();
                    if (s.TryGetProperty("height", out var h)) height = h.GetInt32();
                    if (s.TryGetProperty("codec_name", out var cn)) codec = cn.GetString();
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
                TimeSpan.FromSeconds(Math.Max(0, durationSec)), bitrate, codec);
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
    /// 按设置的性能档位决定并行路数：单路直接整段转码；多路按时长等分多段、
    /// 多 ffmpeg 进程并行转码，再 concat 无损拼接（v1.12.0）。
    /// 每段/整段均先尝试 NVENC 硬编码，失败自动回退 x264；成功后写 meta 并返回结果。
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

        var parallelism = ProxyPerformance.FromId(_settings.ProxyPerformance).Parallelism;
        var ranges = ComputeChunkRanges(probe.Duration, parallelism);
        var usedHardware = ranges.Count <= 1
            ? GenerateSingle(sourcePath, partPath, actual, probe, onProgress, ct)
            : GenerateParallel(sourcePath, dir, partPath, actual, probe, ranges, onProgress, ct);

        // 成功后才落成正式文件名，避免半成品被当成有效代理
        if (File.Exists(finalPath)) SafeDelete(finalPath);
        File.Move(partPath, finalPath);
        WriteMeta(dir, sourcePath, actual, probe);
        return new ProxyGenerationResult(finalPath, actual, usedHardware);
    }

    /// <summary>
    /// 按时长等分转码分段（供并行转码）。规则：
    /// 请求路数 ≤1、时长未知（≤0）或时长过短（每段不足约 60 秒）时回落到单段。
    /// 返回值 = 各分段的（起点, 时长），首尾相接且覆盖全长。
    /// </summary>
    internal static IReadOnlyList<(TimeSpan Start, TimeSpan Duration)> ComputeChunkRanges(
        TimeSpan duration, int requestedParallelism)
    {
        static IReadOnlyList<(TimeSpan, TimeSpan)> Single(TimeSpan d) =>
            new[] { (TimeSpan.Zero, d) };

        if (requestedParallelism <= 1 || duration <= TimeSpan.Zero) return Single(duration);
        // 每段至少约 60 秒，避免碎片化带来的开销大于收益
        var maxByDuration = Math.Max(1, (int)(duration.TotalSeconds / 60));
        var n = Math.Clamp(requestedParallelism, 1, maxByDuration);
        if (n <= 1) return Single(duration);

        var ranges = new List<(TimeSpan, TimeSpan)>(n);
        var step = TimeSpan.FromTicks(duration.Ticks / n);
        for (var i = 0; i < n; i++)
        {
            var start = TimeSpan.FromTicks(step.Ticks * i);
            var end = i == n - 1 ? duration : TimeSpan.FromTicks(step.Ticks * (i + 1));
            ranges.Add((start, end - start));
        }
        return ranges;
    }

    /// <summary>单路整段转码。返回是否用了硬编码。</summary>
    private bool GenerateSingle(
        string sourcePath, string partPath, ProxyPreset preset, MediaProbeResult probe,
        Action<double>? onProgress, CancellationToken ct)
    {
        try
        {
            return TranscodeWithFallback(sourcePath, partPath, preset, probe,
                probe.Duration.TotalMilliseconds, onProgress, ct, null, null);
        }
        catch (OperationCanceledException)
        {
            SafeDelete(partPath);
            throw;
        }
    }

    /// <summary>
    /// 统一的回退链（单路/分段共用）：NVENC + 按编码格式选定的解码方式 →
    /// NVENC + 翻转解码方式 → libx264 软解软编。取消在任何一步都直接向上抛。
    /// 返回最终是否用了硬编码。
    /// </summary>
    private bool TranscodeWithFallback(
        string sourcePath, string output, ProxyPreset preset, MediaProbeResult probe,
        double durationMs, Action<double>? onProgress, CancellationToken ct,
        TimeSpan? chunkStart, TimeSpan? chunkDuration)
    {
        var hwDecode = probe.PreferHardwareDecode;
        // ① NVENC + 首选解码方式（H.264 软解更快；HEVC 等硬解可快 2 倍以上，见 v1.12.0 实测）
        try
        {
            RunOnce(hardware: true, hwDecode);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ProxyMedia] NVENC(hwdec={hwDecode}) 失败，翻转解码方式重试: {ex.Message}");
            SafeDelete(output);
        }
        // ② NVENC + 翻转解码方式（覆盖「探测的编码格式判断失误/驱动组合」等情况）
        try
        {
            RunOnce(hardware: true, !hwDecode);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ProxyMedia] NVENC(hwdec={!hwDecode}) 仍失败，回退 x264: {ex.Message}");
            SafeDelete(output);
        }
        // ③ x264 软编（无 NVIDIA 显卡/驱动不支持 NVENC 时的兜底）
        RunOnce(hardware: false, hardwareDecode: false);
        return false;

        void RunOnce(bool hardware, bool hardwareDecode)
        {
            _ffmpeg.GenerateProxy(sourcePath, output, preset, hardware, durationMs, ct,
                onProgress, chunkStart, chunkDuration, hardwareDecode);
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
                throw new InvalidOperationException("代理输出未生成或长度为 0。");
        }
    }

    /// <summary>
    /// 多路并行：分段转码到 chunks\ 子目录，全部成功后 concat 无损拼接。
    /// 每段独立 NVENC→x264 回退（NVENC 并发会话有上限，超限只影响该段）。
    /// 取消或失败时清理全部分段与半成品。
    /// </summary>
    private bool GenerateParallel(
        string sourcePath, string dir, string partPath, ProxyPreset preset, MediaProbeResult probe,
        IReadOnlyList<(TimeSpan Start, TimeSpan Duration)> ranges,
        Action<double>? onProgress, CancellationToken ct)
    {
        var chunkDir = Path.Combine(dir, "chunks");
        var listPath = Path.Combine(dir, "chunks.txt");
        var progresses = new double[ranges.Count];
        var usedHardware = new bool[ranges.Count];
        try
        {
            Directory.CreateDirectory(chunkDir);
            var tasks = new Task[ranges.Count];
            for (var i = 0; i < ranges.Count; i++)
            {
                var index = i;
                tasks[i] = Task.Run(() =>
                {
                    var (start, dur) = ranges[index];
                    var output = Path.Combine(chunkDir, $"chunk-{index:D3}.mp4");
                    try
                    {
                        usedHardware[index] = TranscodeWithFallback(sourcePath, output, preset,
                            probe, dur.TotalMilliseconds,
                            p =>
                            {
                                progresses[index] = p;
                                onProgress?.Invoke(progresses.Average());
                            }, ct, start, dur);
                    }
                    catch
                    {
                        SafeDelete(output);
                        throw;
                    }
                }, ct);
            }
            WaitAll(tasks, ct);

            // concat 无损拼接（分段同为 H.264/AAC 且参数一致，可直接流复制）
            WriteConcatList(listPath, ranges.Count,
                i => Path.Combine(chunkDir, $"chunk-{i:D3}.mp4"));
            var totalMs = ranges.Aggregate(0.0, (s, r) => s + r.Duration.TotalMilliseconds);
            _ffmpeg.ConcatFiles(listPath, partPath, totalMs, ct);
            if (!File.Exists(partPath) || new FileInfo(partPath).Length == 0)
                throw new InvalidOperationException("代理文件拼接失败（未生成或长度为 0）。");
            return usedHardware.All(h => h);
        }
        catch
        {
            SafeDelete(partPath); // 拼接半成品不留
            throw;
        }
        finally
        {
            // 分段与清单是一次性中间产物：无论成功/失败/取消都清理
            SafeDelete(listPath);
            try { if (Directory.Exists(chunkDir)) Directory.Delete(chunkDir, recursive: true); }
            catch { /* 占用中则留待下次缓存清理 */ }
        }
    }

    /// <summary>等待全部并行任务；取消时聚合为 OperationCanceledException。</summary>
    private static void WaitAll(Task[] tasks, CancellationToken ct)
    {
        try
        {
            Task.WaitAll(tasks, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AggregateException ex) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException("代理生成已取消。", ex, ct);
        }
        catch (AggregateException ex)
        {
            // 展开首个真实异常，避免上层只看到 AggregateException
            throw ex.Flatten().InnerExceptions.First();
        }
    }

    /// <summary>写 concat demuxer 清单（UTF-8 无 BOM，单引号按 ffmpeg 规则转义）。</summary>
    private static void WriteConcatList(string listPath, int count, Func<int, string> pathOf)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < count; i++)
        {
            var p = pathOf(i).Replace("'", "'\\''");
            sb.Append("file '").Append(p).Append("'\n");
        }
        File.WriteAllText(listPath, sb.ToString(), new System.Text.UTF8Encoding(false));
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
