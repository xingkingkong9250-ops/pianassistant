using System.IO;
using System.Text;
using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// 片段拼接服务：先用 <see cref="ClipExtractionService"/> 以流复制方式把每段
/// 导出为临时文件，再用 ffmpeg concat demuxer（-f concat -safe 0 -c copy）无损拼接。
/// 全部片段必须来自同一源视频，否则抛 <see cref="InvalidOperationException"/>。
/// </summary>
public sealed class ConcatService
{
    private readonly FfmpegRunner _ffmpeg;
    private readonly AppSettingsService? _settings;

    public ConcatService(FfmpegRunner ffmpeg, AppSettingsService? settings = null)
    {
        ArgumentNullException.ThrowIfNull(ffmpeg);
        _ffmpeg = ffmpeg;
        _settings = settings;
    }

    /// <summary>
    /// 拼接多个片段为一个视频文件。
    /// 进度按「N 段导出 + 1 次拼接」共 N+1 步折算到 0..1。
    /// 临时文件与清单在 finally 中清理，输出文件保留。
    /// </summary>
    public async Task<string> ConcatAsync(
        IReadOnlyList<ClipRange> ranges,
        string outputPath,
        CancellationToken cancellationToken,
        Action<double>? onProgress = null)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (ranges.Count == 0)
            throw new InvalidOperationException("没有可拼接的片段。");
        ValidateSameSource(ranges);

        // 确保输出目录存在
        var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        var tempRoot = Path.Combine(Path.GetTempPath(), $"pian-concat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var totalSteps = ranges.Count + 1;
            var tempClips = new List<string>(ranges.Count);

            // 第 1..N 步：逐段导出到临时目录（按导出预设统一格式/分辨率）
            var tempExtractor = new ClipExtractionService(_ffmpeg, tempRoot, settings: _settings);
            for (int i = 0; i < ranges.Count; i++)
            {
                var index = i;
                var baseRatio = (double)i / totalSteps;
                var tempPath = await Task.Run(
                    () => tempExtractor.Extract(
                        ranges[index], false, cancellationToken,
                        p => onProgress?.Invoke(baseRatio + p / totalSteps),
                        // 临时片段不带字幕：各段字幕轨不一致会阻塞 concat 拼接（v1.15.6）
                        includeSubtitles: false),
                    cancellationToken).ConfigureAwait(false);
                tempClips.Add(tempPath);
                onProgress?.Invoke((double)(i + 1) / totalSteps);
            }

            // 临时 list.txt：UTF-8（无 BOM）；concat demuxer 无法解析带 BOM 的首行
            var listPath = Path.Combine(tempRoot, "list.txt");
            await File.WriteAllTextAsync(
                listPath, BuildListFileContent(tempClips), new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);

            // 第 N+1 步：concat demuxer 无损拼接
            var totalMs = ranges.Sum(r => r.Duration!.Value.TotalMilliseconds);
            await Task.Run(
                () => _ffmpeg.ConcatFiles(
                    listPath, outputPath, totalMs, cancellationToken,
                    p => onProgress?.Invoke((double)ranges.Count / totalSteps + p / totalSteps)),
                cancellationToken).ConfigureAwait(false);
            onProgress?.Invoke(1);
            return outputPath;
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { /* 清理失败不影响拼接结果 */ }
        }
    }

    /// <summary>
    /// 校验全部片段同源且完整（有出点且出点晚于入点），否则抛
    /// <see cref="InvalidOperationException"/>（拼接要求编码参数一致，跨源不保证无损）。
    /// </summary>
    private static void ValidateSameSource(IReadOnlyList<ClipRange> ranges)
    {
        var first = ranges[0];
        if (!first.IsComplete)
            throw new InvalidOperationException("第 1 个片段不完整：缺少出点或出点早于入点。");
        for (int i = 1; i < ranges.Count; i++)
        {
            if (!ranges[i].IsComplete)
                throw new InvalidOperationException($"第 {i + 1} 个片段不完整：缺少出点或出点早于入点。");
            if (!string.Equals(ranges[i].SourcePath, first.SourcePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "全部片段必须来自同一源视频才能拼接：" +
                    $"{Path.GetFileName(first.SourcePath)} 与 {Path.GetFileName(ranges[i].SourcePath)} 不同。");
        }
    }

    /// <summary>
    /// 生成 concat demuxer 清单内容：每行 <c>file '路径'</c>，
    /// 路径中的单引号按 ffmpeg 规则转义为 '\''。
    /// </summary>
    public static string BuildListFileContent(IReadOnlyList<string> clipPaths)
    {
        ArgumentNullException.ThrowIfNull(clipPaths);
        var sb = new StringBuilder();
        foreach (var path in clipPaths)
        {
            sb.Append("file '").Append(path.Replace("'", "'\\''", StringComparison.Ordinal)).AppendLine("'");
        }
        return sb.ToString();
    }
}
