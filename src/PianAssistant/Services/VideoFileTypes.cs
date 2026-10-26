using System.IO;

namespace PianAssistant.Services;

/// <summary>
/// 支持的视频文件类型清单（v1.14.0 单一口径）。
/// 打开对话框 Filter、拖放校验、空态点击打开等全部共用此处，
/// 避免 App.xaml.cs 与 MainWindow.xaml.cs 各自硬编码导致口径漂移。
/// </summary>
public static class VideoFileTypes
{
    /// <summary>支持的视频扩展名（含点，有序——对话框 Filter 展示顺序稳定）。</summary>
    public static readonly string[] Extensions =
        [".mp4", ".mkv", ".avi", ".mov", ".flv", ".wmv", ".webm", ".ts", ".m2ts", ".mpg"];

    /// <summary>扩展名查找集（大小写不敏感）。</summary>
    private static readonly HashSet<string> Lookup = new(Extensions, StringComparer.OrdinalIgnoreCase);

    /// <summary>打开文件对话框用的 Filter 字符串（由扩展名清单生成）。</summary>
    public static readonly string DialogFilter =
        "视频文件|" + string.Join(';', Extensions.Select(e => "*" + e)) + "|所有文件|*.*";

    /// <summary>判断文件路径是否为受支持的视频文件（按扩展名，大小写不敏感）。</summary>
    public static bool IsVideoFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Lookup.Contains(Path.GetExtension(path));
    }
}
