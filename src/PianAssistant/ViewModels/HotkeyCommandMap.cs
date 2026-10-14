using System.Windows.Input;
using PianAssistant.Models;

namespace PianAssistant.ViewModels;

/// <summary>
/// 一条热键映射：目标命令 + 可选命令参数（如百分比跳转的比例）。
/// </summary>
public readonly record struct HotkeyBinding(ICommand Command, object? Parameter);

/// <summary>
/// 纯映射表：把 <see cref="HotkeyAction"/> 映射到具体的 <see cref="ICommand"/>。
/// 抽出为不依赖 <see cref="ShellViewModel"/> 的可测类，供
/// <c>MainWindow</c> 动态重建 InputBindings 时查询。
/// <para>
/// 关键约束：<see cref="PianAssistant.Services.HotkeyService.Defaults"/> 中的每条动作
/// 都必须在此解析到非 null 的命令（由单元测试锁定），不得保留"假快捷键"。
/// </para>
/// </summary>
public sealed class HotkeyCommandMap
{
    private readonly Dictionary<HotkeyAction, HotkeyBinding> _bindings = new();

    public HotkeyCommandMap(
        PlayerViewModel player,
        ICommand toggleSidebarPanel,
        ICommand previousNote,
        ICommand nextNote)
    {
        ArgumentNullException.ThrowIfNull(player);

        void Add(HotkeyAction action, ICommand command, object? parameter = null) =>
            _bindings[action] = new HotkeyBinding(command, parameter);

        // 播放控制
        Add(HotkeyAction.PlayPause, player.PlayPauseCommand);
        Add(HotkeyAction.NextFrame, player.NextFrameCommand);
        Add(HotkeyAction.PreviousFrame, player.PreviousFrameCommand);
        Add(HotkeyAction.SeekBackward1Second, player.SeekBackward1SecondCommand);
        Add(HotkeyAction.SeekForward1Second, player.SeekForward1SecondCommand);
        Add(HotkeyAction.SeekBackward10Seconds, player.SeekBackward10SecondsCommand);
        Add(HotkeyAction.SeekForward10Seconds, player.SeekForward10SecondsCommand);
        Add(HotkeyAction.SeekToStart, player.SeekToStartCommand);
        Add(HotkeyAction.SeekToEnd, player.SeekToEndCommand);
        Add(HotkeyAction.IncreaseRate, player.IncreaseRateCommand);
        Add(HotkeyAction.DecreaseRate, player.DecreaseRateCommand);
        // 入出点
        Add(HotkeyAction.MarkIn, player.MarkInCommand);
        Add(HotkeyAction.MarkOut, player.MarkOutCommand);
        Add(HotkeyAction.ClearInOut, player.ClearInOutCommand);
        // 截图 / 片段 / 裁切
        Add(HotkeyAction.CaptureScreenshot, player.CaptureScreenshotCommand);
        Add(HotkeyAction.ExtractClip, player.ExtractClipCommand);
        Add(HotkeyAction.AddCutPoint, player.AddCutPointCommand);
        Add(HotkeyAction.RemoveLastCutPoint, player.RemoveLastCutPointCommand);
        Add(HotkeyAction.ClearCutPoints, player.ClearCutPointsCommand);
        Add(HotkeyAction.ExportSelectedSegment, player.ExportSelectedSegmentCommand);
        // 视图 / 音量
        Add(HotkeyAction.ToggleFullscreen, player.ToggleFullscreenCommand);
        Add(HotkeyAction.ExitFullscreen, player.ExitFullscreenCommand);
        Add(HotkeyAction.TogglePlayPauseLoop, player.ToggleLoopCommand);
        Add(HotkeyAction.ToggleMute, player.ToggleMuteCommand);
        Add(HotkeyAction.VolumeUp, player.VolumeUpCommand);
        Add(HotkeyAction.VolumeDown, player.VolumeDownCommand);
        // D1~D9 百分比跳转
        Add(HotkeyAction.SeekPercent10, player.SeekPercentCommand, 0.10);
        Add(HotkeyAction.SeekPercent20, player.SeekPercentCommand, 0.20);
        Add(HotkeyAction.SeekPercent30, player.SeekPercentCommand, 0.30);
        Add(HotkeyAction.SeekPercent40, player.SeekPercentCommand, 0.40);
        Add(HotkeyAction.SeekPercent50, player.SeekPercentCommand, 0.50);
        Add(HotkeyAction.SeekPercent60, player.SeekPercentCommand, 0.60);
        Add(HotkeyAction.SeekPercent70, player.SeekPercentCommand, 0.70);
        Add(HotkeyAction.SeekPercent80, player.SeekPercentCommand, 0.80);
        Add(HotkeyAction.SeekPercent90, player.SeekPercentCommand, 0.90);
        // 面板 / 标记跳转
        Add(HotkeyAction.ToggleSidebarPanel, toggleSidebarPanel);
        Add(HotkeyAction.PreviousNote, previousNote);
        Add(HotkeyAction.NextNote, nextNote);
    }

    /// <summary>返回动作对应的命令绑定；未映射返回 null。</summary>
    public HotkeyBinding? Resolve(HotkeyAction action) =>
        _bindings.TryGetValue(action, out var binding) ? binding : null;

    /// <summary>按动作名解析命令（供一致性测试断言）。</summary>
    public ICommand? ResolveCommand(string actionName) =>
        Enum.TryParse<HotkeyAction>(actionName, out var action)
            ? Resolve(action)?.Command
            : null;
}
