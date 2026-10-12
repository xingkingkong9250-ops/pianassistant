namespace PianAssistant.Models;

/// <summary>
/// 笔记模板：为新增笔记自动附加分类前缀（如 "[构图] "）。
/// </summary>
public sealed class NoteTemplate
{
    public string Name { get; }
    public string Prefix { get; }

    public NoteTemplate(string name, string prefix)
    {
        Name = name;
        Prefix = prefix;
    }

    public override string ToString() => Name;

    /// <summary>「无模板」哨兵：空前缀，AddNote 时不附加任何内容。</summary>
    public static readonly NoteTemplate None = new("无模板", string.Empty);

    /// <summary>内置六类拉片模板。</summary>
    public static IReadOnlyList<NoteTemplate> Presets { get; } = new List<NoteTemplate>
    {
        new("构图", "[构图] "),
        new("灯光", "[灯光] "),
        new("节奏", "[节奏] "),
        new("转场", "[转场] "),
        new("表演", "[表演] "),
        new("剪辑", "[剪辑] "),
    };
}
