using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// 项目保存/加载/导入导出服务。
/// 负责在当前会话（VM 持有的截图/片段/笔记）与 SQLite 之间同步数据，
/// 并以 zip 打包项目用于跨机器传递。
/// </summary>
public sealed class ProjectService
{
    private const string ManifestEntry = "project.json";
    private const string ScreenshotsFolder = "screenshots";
    private const string ClipsFolder = "clips";

    private readonly ProjectDatabase _database;
    private readonly ProjectRepository _repository;
    private readonly AppSettingsService _settings;

    // --- 会话访问器 ---
    // 由 ShellViewModel 在构造后注入，用于读取/写入当前会话的内存数据。
    // MVVM 友好：服务不直接依赖 ViewModel 类型，仅依赖委托契约。

    /// <summary>获取当前源视频路径（可能为 null）。</summary>
    public Func<string?>? ResolveSourcePath { get; set; }

    /// <summary>获取当前会话的全部截图。</summary>
    public Func<IReadOnlyList<ScreenshotEntry>>? ResolveScreenshots { get; set; }

    /// <summary>获取当前会话的全部片段（含输出路径）。</summary>
    public Func<IReadOnlyList<(ClipRange range, string? outputPath)>>? ResolveClips { get; set; }

    /// <summary>获取当前会话的全部笔记。</summary>
    public Func<IReadOnlyList<NoteEntry>>? ResolveNotes { get; set; }

    /// <summary>将截图列表应用到 VM（先清空再填充）。</summary>
    public Action<IReadOnlyList<ScreenshotEntry>>? ApplyScreenshots { get; set; }

    /// <summary>将片段列表应用到 VM（先清空再填充）。</summary>
    public Action<IReadOnlyList<(ClipRange range, string? outputPath)>>? ApplyClips { get; set; }

    /// <summary>将笔记列表应用到 VM（先清空再填充）。</summary>
    public Action<IReadOnlyList<NoteEntry>>? ApplyNotes { get; set; }

    /// <summary>通知 Shell 打开指定源视频。</summary>
    public Action<string?>? ApplySourcePath { get; set; }

    public ProjectService(ProjectDatabase database, ProjectRepository repository, AppSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(settings);
        _database = database;
        _repository = repository;
        _settings = settings;
    }

    // ==================== 保存 ====================

    /// <summary>
    /// 保存当前会话的所有截图/片段/笔记到数据库。
    /// 返回新创建的项目 ID；失败返回 null（不抛出到 UI）。
    /// </summary>
    public string? SaveProject(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        try
        {
            return SaveProjectCore(name);
        }
        catch (Exception ex)
        {
            AppLog.Error("[ProjectService] 保存项目失败", ex);
            return null;
        }
    }

    private string? SaveProjectCore(string name)
    {
        var sourcePath = ResolveSourcePath?.Invoke();
        var project = _repository.CreateProject(name, sourcePath ?? string.Empty);
        if (project is null) return null;

        if (ResolveScreenshots is { } screenshots)
            foreach (var s in screenshots())
                _repository.AddScreenshot(s, project.Id.ToString());

        if (ResolveClips is { } clips)
            foreach (var (range, outputPath) in clips())
                _repository.AddClip(range, outputPath ?? string.Empty, project.Id.ToString());

        if (ResolveNotes is { } notes)
            foreach (var n in notes())
                _repository.AddNote(n, project.Id.ToString());

        return project.Id.ToString();
    }

    // ==================== 加载 ====================

    /// <summary>
    /// 从数据库加载项目的全部数据到 VM（通过 Apply* 委托）。
    /// 任何异常都被捕获为友好失败结果，绝不抛到 UI 崩溃，也不覆盖/删除原始文件。
    /// </summary>
    public ProjectOperationResult LoadProject(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        try
        {
            var project = _repository.GetProject(projectId);
            if (project is null)
                return ProjectOperationResult.Fail("项目不存在或已被删除。");

            ApplySourcePath?.Invoke(project.SourcePath);
            ApplyScreenshots?.Invoke(_repository.GetScreenshots(projectId));
            ApplyClips?.Invoke(_repository.GetClipsWithOutput(projectId));
            ApplyNotes?.Invoke(_repository.GetNotes(projectId));
            return ProjectOperationResult.Ok(projectId);
        }
        catch (Exception ex)
        {
            AppLog.Error($"[ProjectService] 加载项目 {projectId} 失败", ex);
            return ProjectOperationResult.Fail("项目文件已损坏，无法加载；原始文件未被修改。");
        }
    }

    // ==================== 导出 ====================

    /// <summary>
    /// 打包项目的 DB 数据 + 截图/片段文件为 zip。
    /// zip 内含 project.json（清单）以及 screenshots/、clips/ 子目录。
    /// </summary>
    public void ExportProject(string projectId, string zipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        var project = _repository.GetProject(projectId)
            ?? throw new InvalidOperationException($"项目 {projectId} 不存在。");

        var screenshots = _repository.GetScreenshots(projectId);
        var clips = _repository.GetClipsWithOutput(projectId);
        var notes = _repository.GetNotes(projectId);

        var dir = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        // 写入清单。
        var manifest = new ProjectManifest
        {
            Project = new ProjectManifest.ProjectDto
            {
                Name = project.Name,
                SourcePath = project.SourcePath,
                CreatedAt = project.CreatedAt,
                UpdatedAt = project.UpdatedAt,
            },
            Screenshots = screenshots.Select(s => new ProjectManifest.ScreenshotDto
            {
                SourcePath = s.SourcePath,
                OutputFile = TryCopyAndGetRelative(zip, ScreenshotsFolder, s.OutputPath),
                PositionMs = (long)s.Position.TotalMilliseconds,
                CapturedAt = s.CapturedAt,
                Label = s.Label,
            }).ToList(),
            Clips = clips.Select(c => new ProjectManifest.ClipDto
            {
                SourcePath = c.range.SourcePath,
                OutputFile = c.outputPath is null ? null : TryCopyAndGetRelative(zip, ClipsFolder, c.outputPath),
                InPointMs = (long)c.range.InPoint.TotalMilliseconds,
                OutPointMs = c.range.OutPoint is { } o ? (long)o.TotalMilliseconds : null,
                Label = c.range.Label,
                CreatedAt = DateTime.Now,
            }).ToList(),
            Notes = notes.Select(n => new ProjectManifest.NoteDto
            {
                SourcePath = n.SourcePath,
                PositionMs = (long)n.Position.TotalMilliseconds,
                Text = n.Text,
                CreatedAt = n.CreatedAt,
                UpdatedAt = n.UpdatedAt,
            }).ToList(),
        };

        var json = JsonSerializer.Serialize(manifest, ProjectManifestContext.Default.ProjectManifest);
        WriteEntry(zip, ManifestEntry, json);
    }

    // ==================== 导入 ====================

    /// <summary>
    /// 解压并导入项目：读取清单，复制截图/片段文件到应用输出目录，
    /// 在 DB 中创建新项目及全部记录。返回结果（含新项目 ID）；
    /// 项目包损坏/格式非法时返回友好失败，不抛到 UI，也不修改任何现有数据。
    /// </summary>
    public ProjectOperationResult ImportProject(string zipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        try
        {
            return ImportProjectCore(zipPath);
        }
        catch (InvalidDataException ex)
        {
            AppLog.Error("[ProjectService] 项目包损坏", ex);
            return ProjectOperationResult.Fail("项目包已损坏或格式不正确，无法导入。");
        }
        catch (Exception ex)
        {
            AppLog.Error("[ProjectService] 导入项目失败", ex);
            return ProjectOperationResult.Fail("导入项目失败：" + ex.Message);
        }
    }

    private ProjectOperationResult ImportProjectCore(string zipPath)
    {
        if (!File.Exists(zipPath))
            return ProjectOperationResult.Fail("项目包文件不存在。");

        using var zip = ZipFile.OpenRead(zipPath);
        var manifestEntry = zip.GetEntry(ManifestEntry)
            ?? throw new InvalidOperationException("项目包缺少 project.json 清单。");
        using var stream = manifestEntry.Open();
        var manifest = JsonSerializer.Deserialize(stream, ProjectManifestContext.Default.ProjectManifest)
            ?? throw new InvalidOperationException("项目清单内容无效。");

        var screenshotsDir = _settings.ScreenshotsDirectory;
        var clipsDir = _settings.ClipsDirectory;
        Directory.CreateDirectory(screenshotsDir);
        Directory.CreateDirectory(clipsDir);

        // 创建新项目。
        var project = _repository.CreateProject(manifest.Project.Name, manifest.Project.SourcePath ?? string.Empty);
        if (project is null)
            return ProjectOperationResult.Fail("数据库写入失败，导入中止。");

        // 导入截图。
        foreach (var s in manifest.Screenshots)
        {
            var outputPath = ExtractAsset(zip, s.OutputFile, screenshotsDir);
            var entry = new ScreenshotEntry(
                s.SourcePath, outputPath,
                TimeSpan.FromMilliseconds(s.PositionMs),
                s.CapturedAt, s.Label);
            _repository.AddScreenshot(entry, project.Id.ToString());
        }

        // 导入片段。
        foreach (var c in manifest.Clips)
        {
            var outputPath = c.OutputFile is null ? null : ExtractAsset(zip, c.OutputFile, clipsDir);
            var range = new ClipRange(
                c.SourcePath,
                TimeSpan.FromMilliseconds(c.InPointMs),
                c.OutPointMs is { } o ? TimeSpan.FromMilliseconds(o) : null,
                c.Label);
            _repository.AddClip(range, outputPath ?? string.Empty, project.Id.ToString());
        }

        // 导入笔记。
        foreach (var n in manifest.Notes)
        {
            var note = new NoteEntry(
                n.SourcePath,
                TimeSpan.FromMilliseconds(n.PositionMs),
                n.Text,
                n.CreatedAt);
            note.UpdatedAt = n.UpdatedAt;
            _repository.AddNote(note, project.Id.ToString());
        }

        return ProjectOperationResult.Ok(project.Id.ToString());
    }

    // ==================== 内部辅助 ====================

    /// <summary>
    /// 将磁盘文件复制到 zip 内指定子目录，返回 zip 内的相对路径。
    /// 文件不存在时跳过并返回 null（不阻断导出）。
    /// </summary>
    private static string? TryCopyAndGetRelative(ZipArchive zip, string folder, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;
        var entryName = $"{folder}/{Path.GetFileName(filePath)}";
        var entry = zip.CreateEntry(entryName);
        using var src = File.OpenRead(filePath);
        using var dst = entry.Open();
        src.CopyTo(dst);
        return entryName;
    }

    /// <summary>将 zip 内的资产文件解压到目标目录，返回解压后的绝对路径。</summary>
    private static string ExtractAsset(ZipArchive zip, string? relativePath, string targetDir)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return string.Empty;
        var entry = zip.GetEntry(relativePath);
        if (entry is null) return string.Empty;
        var fileName = Path.GetFileName(relativePath);
        var destPath = Path.Combine(targetDir, fileName);
        // 避免文件名冲突。
        destPath = MakeUnique(destPath);
        using var src = entry.Open();
        using var dst = new FileStream(destPath, FileMode.CreateNew, FileAccess.Write);
        src.CopyTo(dst);
        return destPath;
    }

    private static void WriteEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string MakeUnique(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return path + ".unique";
    }
}

/// <summary>
/// 项目操作结果：成功标志 + 友好错误消息（失败时）+ 关联项目 ID。
/// 供 ViewModel 层据此弹出友好提示，避免异常直接冒泡到 UI。
/// </summary>
public readonly record struct ProjectOperationResult(bool Success, string? Error, string? ProjectId)
{
    public static ProjectOperationResult Ok(string? projectId = null) => new(true, null, projectId);
    public static ProjectOperationResult Fail(string message) => new(false, message, null);
}

// ==================== 导出/导入清单 DTO ====================

internal sealed class ProjectManifest
{
    public ProjectDto Project { get; set; } = new();
    public List<ScreenshotDto> Screenshots { get; set; } = new();
    public List<ClipDto> Clips { get; set; } = new();
    public List<NoteDto> Notes { get; set; } = new();

    public sealed class ProjectDto
    {
        public string Name { get; set; } = string.Empty;
        public string? SourcePath { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class ScreenshotDto
    {
        public string SourcePath { get; set; } = string.Empty;
        public string? OutputFile { get; set; }
        public long PositionMs { get; set; }
        public DateTime CapturedAt { get; set; }
        public string? Label { get; set; }
    }

    public sealed class ClipDto
    {
        public string SourcePath { get; set; } = string.Empty;
        public string? OutputFile { get; set; }
        public long InPointMs { get; set; }
        public long? OutPointMs { get; set; }
        public string? Label { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class NoteDto
    {
        public string SourcePath { get; set; } = string.Empty;
        public long PositionMs { get; set; }
        public string Text { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}

[JsonSerializable(typeof(ProjectManifest))]
internal sealed partial class ProjectManifestContext : JsonSerializerContext
{
}
