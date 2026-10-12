using System.Globalization;
using System.IO;
using System.Text;

namespace PianAssistant.Services;

/// <summary>
/// 输出目录规则解析（纯逻辑，便于单元测试）：
/// 基目录 +（可选）yyyyMMdd 日期子目录 +（可选）项目名子目录。
/// 规则：<c>ScreenshotsDirectory/[yyyyMMdd]/[项目名]/</c> 与
/// <c>ClipsDirectory/[yyyyMMdd]/[项目名]/</c>。
/// </summary>
public static class OutputDirectoryResolver
{
    /// <summary>
    /// 按组织选项解析输出子目录。调用方负责 <see cref="Directory.CreateDirectory"/>。
    /// </summary>
    /// <param name="baseDirectory">基础输出目录（截图/片段目录）。</param>
    /// <param name="organizeByDate">是否按日期（yyyyMMdd）分子目录。</param>
    /// <param name="projectSubfolder">项目名子目录；空白视为未启用。</param>
    /// <param name="timestamp">用于日期子目录的时间戳。</param>
    public static string Resolve(
        string baseDirectory,
        bool organizeByDate,
        string? projectSubfolder,
        DateTime timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var dir = baseDirectory;
        if (organizeByDate)
            dir = Path.Combine(dir, timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        var project = Sanitize(projectSubfolder);
        if (project is not null)
            dir = Path.Combine(dir, project);
        return dir;
    }

    /// <summary>
    /// 清理子目录名：去除首尾空白、把文件名非法字符替换为下划线、去尾部点。
    /// 空白或清空后为空返回 null（表示未启用）。
    /// </summary>
    public static string? Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.Trim())
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        var result = sb.ToString().TrimEnd('.');
        return result.Length == 0 ? null : result;
    }
}
