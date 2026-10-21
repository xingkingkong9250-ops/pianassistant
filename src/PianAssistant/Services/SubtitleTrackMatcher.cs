using System.IO;

namespace PianAssistant.Services;

/// <summary>
/// 外挂字幕匹配工具（v1.5.0）：打开视频时按「同目录 + 同主名」规则
/// 自动发现外挂字幕文件。匹配逻辑为纯函数，便于单元测试。
/// </summary>
public static class SubtitleTrackMatcher
{
    /// <summary>支持的外挂字幕扩展名（拖放加载与自动匹配共用同一清单）。</summary>
    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".ass", ".ssa", ".vtt", ".sub"
    };

    /// <summary>判断文件路径是否为受支持的外挂字幕文件（按扩展名，大小写不敏感）。</summary>
    public static bool IsSubtitleFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return SupportedExtensions.Contains(Path.GetExtension(path));
    }

    /// <summary>
    /// 纯函数：从候选文件名中筛出与视频主名匹配的字幕文件名。
    /// 规则：文件名 == 视频主名 + 字幕扩展名（如 movie.srt），
    /// 或以「视频主名.」开头、以字幕扩展名结尾（如 movie.zh.srt / movie.繁体.ass）。
    /// 返回按文件名（序号不敏感）升序排列的结果，保证匹配顺序稳定。
    /// </summary>
    public static IReadOnlyList<string> MatchSubtitleNames(
        string videoFileName, IEnumerable<string> candidateFileNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoFileName);
        ArgumentNullException.ThrowIfNull(candidateFileNames);

        var stem = Path.GetFileNameWithoutExtension(videoFileName);
        if (stem.Length == 0) return Array.Empty<string>();

        var matches = new List<string>();
        foreach (var candidate in candidateFileNames)
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            var ext = Path.GetExtension(candidate);
            if (!SupportedExtensions.Contains(ext)) continue;

            var nameNoExt = Path.GetFileNameWithoutExtension(candidate);
            // 完全同名（movie.srt）或「主名.语言标签」前缀（movie.zh.srt）
            if (string.Equals(nameNoExt, stem, StringComparison.OrdinalIgnoreCase)
                || nameNoExt.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(candidate);
            }
        }
        matches.Sort(StringComparer.OrdinalIgnoreCase);
        return matches;
    }

    /// <summary>
    /// 磁盘包装：查找视频文件同目录下所有匹配的外挂字幕完整路径。
    /// 视频文件不存在或目录不可读时返回空列表。
    /// </summary>
    public static IReadOnlyList<string> FindMatchingSubtitles(string videoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        var directory = Path.GetDirectoryName(videoPath);
        var fileName = Path.GetFileName(videoPath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName)
            || !Directory.Exists(directory))
            return Array.Empty<string>();

        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }

        return MatchSubtitleNames(fileName, candidates.Select(Path.GetFileName)!)
            .Select(name => Path.Combine(directory, name))
            .ToList();
    }
}
