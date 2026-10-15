namespace PianAssistant.ViewModels;

/// <summary>
/// 右侧边栏面板选择器：Notes（笔记）或 Clips（片段批量导出）。
/// 播放列表自 v0.12.0 起固定于主视频窗口左侧，不再参与右侧切换。
/// </summary>
public enum SidebarPanel
{
    Notes,
    Clips,
}
