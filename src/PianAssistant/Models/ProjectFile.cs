using System.Text.Json;
using System.Text.Json.Serialization;

namespace PianAssistant.Models;

/// <summary>
/// 项目元数据 POCO，对应数据库 projects 表与 .paproj 文件格式。
/// M3 持久化：截图/片段/笔记列表改为存入 SQLite，由 ProjectService 装载到 VM。
/// </summary>
public sealed class ProjectFile
{
    /// <summary>.paproj 文件扩展名。</summary>
    public const string FileExtension = ".paproj";

    /// <summary>项目唯一标识。</summary>
    public Guid Id { get; set; }

    /// <summary>项目名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>源视频路径（可空，多视频项目时为 null）。</summary>
    public string? SourcePath { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>最后更新时间。</summary>
    public DateTime UpdatedAt { get; set; }

    public ProjectFile() { }

    public ProjectFile(string name, string? sourcePath = null, Guid? id = null, DateTime? createdAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        SourcePath = sourcePath;
        Id = id ?? Guid.NewGuid();
        var now = createdAt ?? DateTime.Now;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>序列化为 JSON 文本（用于 .paproj 文件）。</summary>
    public string ToJson() =>
        JsonSerializer.Serialize(this, ProjectFileJsonContext.Default.ProjectFile);

    /// <summary>从 JSON 文本反序列化。</summary>
    public static ProjectFile FromJson(string json) =>
        JsonSerializer.Deserialize(json, ProjectFileJsonContext.Default.ProjectFile)
            ?? throw new InvalidOperationException("项目 JSON 内容无效。");
}

[JsonSerializable(typeof(ProjectFile))]
internal sealed partial class ProjectFileJsonContext : JsonSerializerContext
{
}
