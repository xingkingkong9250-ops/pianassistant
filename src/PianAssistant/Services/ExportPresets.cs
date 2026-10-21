namespace PianAssistant.Services;

/// <summary>
/// 导出预设：片段（视频）与截图的格式/分辨率取值范围，以及 ffmpeg 缩放滤镜。
/// 默认预设：视频 mp4 + 1080P；截图 png + 1080P（v1.4.0）。
/// 分辨率按「长边封顶、只缩不放、偶数对齐」处理：源分辨率更小时不放大。
/// </summary>
public static class ExportPresets
{
    /// <summary>源分辨率：不缩放，片段可走流复制（最快、无损）。</summary>
    public const string SourceResolution = "source";
    public const string DefaultVideoFormat = "mp4";
    public const string DefaultVideoResolution = "1080p";
    public const string DefaultSnapshotFormat = "png";
    public const string DefaultSnapshotResolution = "1080p";

    /// <summary>可选视频容器格式（mp4 / mkv / mov）。</summary>
    public static readonly IReadOnlyList<string> VideoFormats = new[] { "mp4", "mkv", "mov" };

    /// <summary>可选分辨率预设（源 / 1080P / 720P / 480P）。</summary>
    public static readonly IReadOnlyList<string> Resolutions =
        new[] { SourceResolution, "1080p", "720p", "480p" };

    public static bool IsValidVideoFormat(string? format) =>
        format is not null && VideoFormats.Contains(format.Trim().ToLowerInvariant());

    public static bool IsValidResolution(string? resolution) =>
        resolution is not null && Resolutions.Contains(resolution.Trim().ToLowerInvariant());

    /// <summary>非法/空白时回退默认 mp4。</summary>
    public static string NormalizeVideoFormat(string? format) =>
        IsValidVideoFormat(format) ? format!.Trim().ToLowerInvariant() : DefaultVideoFormat;

    /// <summary>非法/空白时回退默认 1080p。</summary>
    public static string NormalizeResolution(string? resolution) =>
        IsValidResolution(resolution)
            ? resolution!.Trim().ToLowerInvariant()
            : DefaultVideoResolution;

    public static void EnsureValidVideoFormat(string format)
    {
        if (!IsValidVideoFormat(format))
            throw new ArgumentException(
                $"format must be one of {string.Join('/', VideoFormats)}", nameof(format));
    }

    public static void EnsureValidResolution(string resolution)
    {
        if (!IsValidResolution(resolution))
            throw new ArgumentException(
                $"resolution must be one of {string.Join('/', Resolutions)}", nameof(resolution));
    }

    /// <summary>
    /// 生成 ffmpeg 缩放滤镜：长边封顶、等比缩小、偶数对齐。
    /// <see cref="SourceResolution"/> 返回 null（不缩放，片段可用流复制）。
    /// </summary>
    public static string? BuildScaleFilter(string? resolution)
    {
        var normalized = NormalizeResolution(resolution);
        if (normalized == SourceResolution) return null;
        var longEdge = normalized switch
        {
            "720p" => 1280,
            "480p" => 854,
            _ => 1920, // 1080p（长边 1920）
        };
        // force_original_aspect_ratio=decrease 保证只缩不放；第二段消掉奇数尺寸
        // （libx264 要求宽高为偶数，奇数源直接缩放会失败）。
        return $"scale='min({longEdge},iw)':'min({longEdge},ih)'" +
               ":force_original_aspect_ratio=decrease:flags=lanczos," +
               "scale=trunc(iw/2)*2:trunc(ih/2)*2";
    }
}
