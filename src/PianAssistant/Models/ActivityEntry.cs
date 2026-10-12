using System.ComponentModel;
using PianAssistant.Helpers;

namespace PianAssistant.Models;

/// <summary>活动类型枚举。</summary>
public enum ActivityType
{
    [Description("笔记")]
    Note,
    [Description("截图")]
    Screenshot,
    [Description("片段")]
    Clip,
    [Description("裁切点")]
    CutPoint,
}

/// <summary>
/// 时间轴活动条目：统一表示笔记、截图、片段、裁切点。
/// 按时间位置排序，用于侧边笔记面板的活动流。
/// </summary>
public sealed class ActivityEntry : ObservableObject
{
    public ActivityType Type { get; }
    public TimeSpan Position { get; }
    public string? FilePath { get; }       // 截图/片段输出路径
    public TimeSpan? Duration { get; }      // 片段时长
    public DateTime CreatedAt { get; }

    private string _label;
    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public ActivityEntry(ActivityType type, TimeSpan position, string label,
        string? filePath = null, TimeSpan? duration = null)
    {
        Type = type;
        Position = position;
        _label = label;
        FilePath = filePath;
        Duration = duration;
        CreatedAt = DateTime.Now;
    }

    /// <summary>类型图标。</summary>
    public string TypeIcon => Type switch
    {
        ActivityType.Note => "📝",
        ActivityType.Screenshot => "📷",
        ActivityType.Clip => "✂",
        ActivityType.CutPoint => "🔴",
        _ => "•",
    };

    /// <summary>类型显示色（十六进制）。</summary>
    public string TypeColor => Type switch
    {
        ActivityType.Note => "#569CD6",
        ActivityType.Screenshot => "#4EC9B0",
        ActivityType.Clip => "#CE9178",
        ActivityType.CutPoint => "#E04B4B",
        _ => "#808080",
    };

    /// <summary>位置格式化字符串。</summary>
    public string PositionText => Position.ToString(@"hh\:mm\:ss\.fff");
}
