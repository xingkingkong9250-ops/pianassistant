using System.IO;

namespace PianAssistant.ViewModels;

/// <summary>
/// 最近文件菜单项：显示文件名（Header）+ 完整路径（ToolTip）+ 打开路径。
/// <see cref="Path"/> 为 null 时表示占位项（无最近文件，禁用）。
/// </summary>
public sealed record RecentFileMenuItem(string Header, string? ToolTip, string? Path);

/// <summary>
/// 「打开文件」按钮右键菜单的数据生成器（纯逻辑，便于单测）。
/// 列表为空时返回单个禁用占位项「（无最近文件）」。
/// </summary>
public static class RecentFilesPresenter
{
    /// <summary>菜单最多展示的最近文件数量。</summary>
    public const int MaxItems = 10;

    /// <summary>把最近文件路径列表转换为可点击的菜单项。</summary>
    public static IReadOnlyList<RecentFileMenuItem> Build(IReadOnlyList<string>? recentFiles)
    {
        if (recentFiles is null || recentFiles.Count == 0)
            return new[] { new RecentFileMenuItem("（无最近文件）", null, null) };

        var items = new List<RecentFileMenuItem>();
        foreach (var path in recentFiles)
        {
            if (items.Count >= MaxItems) break;
            if (string.IsNullOrWhiteSpace(path)) continue;
            items.Add(new RecentFileMenuItem(Path.GetFileName(path), path, path));
        }

        // 全部为空白路径时同样回退到占位项
        if (items.Count == 0)
            return new[] { new RecentFileMenuItem("（无最近文件）", null, null) };
        return items;
    }
}
