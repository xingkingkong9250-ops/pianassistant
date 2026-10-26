namespace PianAssistant.Models;

/// <summary>
/// 媒体探测结果（ffprobe 摘要），用于判断是否需要生成代理。
/// 所有字段为 0 表示探测失败（<see cref="Unknown"/>），调用方应视为「不做代理建议」。
/// </summary>
public sealed record MediaProbeResult(int Width, int Height, TimeSpan Duration, long BitrateBps,
    string? CodecName = null)
{
    /// <summary>探测失败占位值。</summary>
    public static readonly MediaProbeResult Unknown = new(0, 0, TimeSpan.Zero, 0);

    /// <summary>探测是否有效（宽高均大于 0）。</summary>
    public bool IsValid => Width > 0 && Height > 0;

    /// <summary>
    /// 是否「超过 1080P」：短边 &gt; 1080 或长边 &gt; 1920。
    /// 横屏 4K/2.7K/1440P 与竖屏 2160×3840 都会命中。
    /// </summary>
    public bool IsAbove1080P
    {
        get
        {
            if (!IsValid) return false;
            var longEdge = Math.Max(Width, Height);
            var shortEdge = Math.Min(Width, Height);
            return longEdge > 1920 || shortEdge > 1080;
        }
    }

    /// <summary>
    /// 转码代理时是否优先用 NVDEC 硬解（v1.12.0 实测）：
    /// H.264 软解本来就快（硬解的显存回传开销反而更慢）；HEVC 等重解码格式硬解可快 2 倍以上。
    /// 编码未知时按 H.264 处理（软解），失败会由上层回退链翻转重试。
    /// </summary>
    public bool PreferHardwareDecode =>
        CodecName is not null &&
        !string.Equals(CodecName, "h264", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 代理档位（预设）：只降分辨率与码率，帧率保持源不变以保证时间码对齐。
/// </summary>
public sealed record ProxyPreset(string Id, int Width, int Height, int VideoBitrateKbps, string DisplayName)
{
    public static readonly ProxyPreset P720 = new("720p", 1280, 720, 2500, "标准 720P");
    public static readonly ProxyPreset P540 = new("540p", 960, 540, 1500, "轻量 540P");
    public static readonly ProxyPreset P360 = new("360p", 640, 360, 800, "快速 360P");

    public static IReadOnlyList<ProxyPreset> All { get; } = new[] { P720, P540, P360 };

    /// <summary>按 Id 解析档位；未知/空白回落到 720P。</summary>
    public static ProxyPreset FromId(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? P720;
}

/// <summary>代理生成结果。</summary>
public sealed record ProxyGenerationResult(string ProxyPath, ProxyPreset Preset, bool UsedHardwareEncoder);

/// <summary>
/// 代理生成性能档位（v1.12.0）：控制并行转码路数。
/// 安静=单进程（占用最低）；均衡=双路并行；极速=四路并行（CPU/GPU 占用最高）。
/// 并行通过「按时长等分多段 → 多 ffmpeg 进程转码 → concat 无损拼接」实现。
/// </summary>
public sealed record ProxyPerformance(string Id, int Parallelism, string DisplayName)
{
    public static readonly ProxyPerformance Quiet = new("quiet", 1, "安静（单进程，占用最低）");
    public static readonly ProxyPerformance Balanced = new("balanced", 2, "均衡（双路并行）");
    public static readonly ProxyPerformance Turbo = new("turbo", 4, "极速（四路并行，占用最高）");

    public static IReadOnlyList<ProxyPerformance> All { get; } = new[] { Quiet, Balanced, Turbo };

    /// <summary>按 Id 解析档位；未知/空白回落到「均衡」。</summary>
    public static ProxyPerformance FromId(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Balanced;
}
