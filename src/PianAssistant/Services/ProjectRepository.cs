using Microsoft.Data.Sqlite;
using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// 项目数据持久化仓储：对 projects/screenshots/clips/notes 表执行 CRUD。
/// 依赖 <see cref="ProjectDatabase"/> 提供连接；所有 SQL 均用参数化防注入。
/// </summary>
public sealed class ProjectRepository
{
    private readonly ProjectDatabase _db;

    public ProjectRepository(ProjectDatabase db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    // ==================== 项目 CRUD ====================

    /// <summary>创建项目并返回 ProjectFile；失败返回 null。</summary>
    public ProjectFile? CreateProject(string name, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var project = new ProjectFile(name, sourcePath);
        try
        {
            using var conn = _db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO projects (id, name, source_path, created_at, updated_at)
                VALUES (@id, @name, @source_path, @created_at, @updated_at)
                """;
            AddParam(cmd, "@id", project.Id.ToString());
            AddParam(cmd, "@name", project.Name);
            AddParam(cmd, "@source_path", project.SourcePath);
            AddParam(cmd, "@created_at", project.CreatedAt.ToString("o"));
            AddParam(cmd, "@updated_at", project.UpdatedAt.ToString("o"));
            cmd.ExecuteNonQuery();
            return project;
        }
        catch (SqliteException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProjectRepository] CreateProject 失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>按 id 获取项目；不存在返回 null。</summary>
    public ProjectFile? GetProject(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, source_path, created_at, updated_at FROM projects WHERE id = @id";
        AddParam(cmd, "@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return ReadProject(reader);
    }

    /// <summary>获取全部项目（按更新时间倒序）。</summary>
    public List<ProjectFile> GetAllProjects()
    {
        var list = new List<ProjectFile>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, source_path, created_at, updated_at FROM projects ORDER BY updated_at DESC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadProject(reader));
        return list;
    }

    /// <summary>更新项目名称/源路径/更新时间。</summary>
    public void UpdateProject(ProjectFile project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.UpdatedAt = DateTime.Now;
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE projects
            SET name = @name, source_path = @source_path, updated_at = @updated_at
            WHERE id = @id
            """;
        AddParam(cmd, "@id", project.Id.ToString());
        AddParam(cmd, "@name", project.Name);
        AddParam(cmd, "@source_path", project.SourcePath);
        AddParam(cmd, "@updated_at", project.UpdatedAt.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>删除项目并级联删除其截图/片段/笔记。</summary>
    public void DeleteProject(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();
        // 先删子表，再删项目本身。
        foreach (var (table, column) in new[]
        {
            ("screenshots", "project_id"),
            ("clips", "project_id"),
            ("notes", "project_id"),
            ("projects", "id"),
        })
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM {table} WHERE {column} = @id";
            AddParam(cmd, "@id", id);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // ==================== 截图 CRUD ====================

    /// <summary>添加截图记录。</summary>
    public void AddScreenshot(ScreenshotEntry entry, string projectId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO screenshots (id, project_id, source_path, output_path, position_ms, captured_at, label)
            VALUES (@id, @project_id, @source_path, @output_path, @position_ms, @captured_at, @label)
            """;
        AddParam(cmd, "@id", Guid.NewGuid().ToString());
        AddParam(cmd, "@project_id", projectId);
        AddParam(cmd, "@source_path", entry.SourcePath);
        AddParam(cmd, "@output_path", entry.OutputPath);
        AddParam(cmd, "@position_ms", (long)entry.Position.TotalMilliseconds);
        AddParam(cmd, "@captured_at", entry.CapturedAt.ToString("o"));
        AddParam(cmd, "@label", entry.Label);
        cmd.ExecuteNonQuery();
    }

    /// <summary>获取项目的全部截图（按位置升序）。</summary>
    public List<ScreenshotEntry> GetScreenshots(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        var list = new List<ScreenshotEntry>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, project_id, source_path, output_path, position_ms, captured_at, label
            FROM screenshots WHERE project_id = @project_id
            ORDER BY position_ms ASC
            """;
        AddParam(cmd, "@project_id", projectId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ScreenshotEntry(
                GetString(reader, "source_path"),
                GetString(reader, "output_path"),
                TimeSpan.FromMilliseconds(GetInt64(reader, "position_ms")),
                ParseDate(GetString(reader, "captured_at")),
                IsDBNullColumn(reader, "label") ? null : GetString(reader, "label"),
                GetString(reader, "id")));
        }
        return list;
    }

    /// <summary>删除截图记录。</summary>
    public void DeleteScreenshot(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM screenshots WHERE id = @id";
        AddParam(cmd, "@id", id);
        cmd.ExecuteNonQuery();
    }

    // ==================== 片段 CRUD ====================

    /// <summary>添加片段记录。</summary>
    public void AddClip(ClipRange clip, string outputPath, string projectId)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO clips (id, project_id, source_path, output_path, in_point_ms, out_point_ms, label, created_at)
            VALUES (@id, @project_id, @source_path, @output_path, @in_point_ms, @out_point_ms, @label, @created_at)
            """;
        AddParam(cmd, "@id", Guid.NewGuid().ToString());
        AddParam(cmd, "@project_id", projectId);
        AddParam(cmd, "@source_path", clip.SourcePath);
        AddParam(cmd, "@output_path", string.IsNullOrWhiteSpace(outputPath) ? null : outputPath);
        AddParam(cmd, "@in_point_ms", (long)clip.InPoint.TotalMilliseconds);
        AddParam(cmd, "@out_point_ms", clip.OutPoint is { } outP ? (long)outP.TotalMilliseconds : (object)DBNull.Value);
        AddParam(cmd, "@label", clip.Label);
        AddParam(cmd, "@created_at", DateTime.Now.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>获取项目的全部片段（按入点升序）。output_path 不在 ClipRange 中，仅用于导出时读取。</summary>
    public List<ClipRange> GetClips(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        var list = new List<ClipRange>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, source_path, in_point_ms, out_point_ms, label
            FROM clips WHERE project_id = @project_id
            ORDER BY in_point_ms ASC
            """;
        AddParam(cmd, "@project_id", projectId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var inPoint = TimeSpan.FromMilliseconds(GetInt64(reader, "in_point_ms"));
            TimeSpan? outPoint = IsDBNullColumn(reader, "out_point_ms")
                ? null
                : TimeSpan.FromMilliseconds(GetInt64(reader, "out_point_ms"));
            var label = IsDBNullColumn(reader, "label") ? null : GetString(reader, "label");
            list.Add(new ClipRange(
                GetString(reader, "source_path"),
                inPoint, outPoint, label,
                GetString(reader, "id")));
        }
        return list;
    }

    /// <summary>
    /// 获取项目的全部片段（含输出路径），按入点升序。
    /// 供 ProjectService 加载/导出使用；ClipRange 本身不携带输出路径。
    /// </summary>
    public List<(ClipRange range, string? outputPath)> GetClipsWithOutput(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        var list = new List<(ClipRange, string?)>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, source_path, output_path, in_point_ms, out_point_ms, label
            FROM clips WHERE project_id = @project_id
            ORDER BY in_point_ms ASC
            """;
        AddParam(cmd, "@project_id", projectId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var inPoint = TimeSpan.FromMilliseconds(GetInt64(reader, "in_point_ms"));
            TimeSpan? outPoint = IsDBNullColumn(reader, "out_point_ms")
                ? null
                : TimeSpan.FromMilliseconds(GetInt64(reader, "out_point_ms"));
            var label = IsDBNullColumn(reader, "label") ? null : GetString(reader, "label");
            var outputPath = IsDBNullColumn(reader, "output_path") ? null : GetString(reader, "output_path");
            list.Add((new ClipRange(
                GetString(reader, "source_path"),
                inPoint, outPoint, label,
                GetString(reader, "id")), outputPath));
        }
        return list;
    }

    /// <summary>删除片段记录。</summary>
    public void DeleteClip(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM clips WHERE id = @id";
        AddParam(cmd, "@id", id);
        cmd.ExecuteNonQuery();
    }

    // ==================== 笔记 CRUD ====================

    /// <summary>添加笔记记录。</summary>
    public void AddNote(NoteEntry note, string projectId)
    {
        ArgumentNullException.ThrowIfNull(note);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO notes (id, project_id, source_path, position_ms, text, created_at, updated_at)
            VALUES (@id, @project_id, @source_path, @position_ms, @text, @created_at, @updated_at)
            """;
        AddParam(cmd, "@id", note.Id ?? Guid.NewGuid().ToString());
        AddParam(cmd, "@project_id", projectId);
        AddParam(cmd, "@source_path", note.SourcePath);
        AddParam(cmd, "@position_ms", (long)note.Position.TotalMilliseconds);
        AddParam(cmd, "@text", note.Text);
        AddParam(cmd, "@created_at", note.CreatedAt.ToString("o"));
        AddParam(cmd, "@updated_at", note.UpdatedAt?.ToString("o") ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>获取项目的全部笔记（按位置升序）。</summary>
    public List<NoteEntry> GetNotes(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        var list = new List<NoteEntry>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, source_path, position_ms, text, created_at, updated_at
            FROM notes WHERE project_id = @project_id
            ORDER BY position_ms ASC
            """;
        AddParam(cmd, "@project_id", projectId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var created = ParseDate(GetString(reader, "created_at"));
            DateTime? updated = IsDBNullColumn(reader, "updated_at")
                ? null
                : ParseDate(GetString(reader, "updated_at"));
            list.Add(new NoteEntry(
                GetString(reader, "source_path"),
                TimeSpan.FromMilliseconds(GetInt64(reader, "position_ms")),
                GetString(reader, "text"),
                created,
                GetString(reader, "id"))
            {
                UpdatedAt = updated,
            });
        }
        return list;
    }

    /// <summary>更新笔记文本与 updated_at（按 Id 定位）。</summary>
    public void UpdateNote(NoteEntry note)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (string.IsNullOrEmpty(note.Id))
            throw new InvalidOperationException("笔记缺少 Id，无法更新。");
        var now = DateTime.Now;
        note.UpdatedAt = now;
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE notes
            SET text = @text, position_ms = @position_ms, updated_at = @updated_at
            WHERE id = @id
            """;
        AddParam(cmd, "@id", note.Id);
        AddParam(cmd, "@text", note.Text);
        AddParam(cmd, "@position_ms", (long)note.Position.TotalMilliseconds);
        AddParam(cmd, "@updated_at", now.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>删除笔记记录。</summary>
    public void DeleteNote(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM notes WHERE id = @id";
        AddParam(cmd, "@id", id);
        cmd.ExecuteNonQuery();
    }

    // ==================== 内部辅助 ====================

    private static ProjectFile ReadProject(SqliteDataReader reader)
    {
        return new ProjectFile(
            GetString(reader, "name"),
            IsDBNullColumn(reader, "source_path") ? null : GetString(reader, "source_path"),
            Guid.Parse(GetString(reader, "id")),
            ParseDate(GetString(reader, "created_at")))
        {
            UpdatedAt = ParseDate(GetString(reader, "updated_at")),
        };
    }

    /// <summary>按列名读取字符串（先 GetOrdinal 再 GetString）。</summary>
    private static string GetString(SqliteDataReader reader, string name) =>
        reader.GetString(reader.GetOrdinal(name));

    /// <summary>按列名读取 Int64。</summary>
    private static long GetInt64(SqliteDataReader reader, string name) =>
        reader.GetInt64(reader.GetOrdinal(name));

    /// <summary>按列名判断是否为 NULL。</summary>
    private static bool IsDBNullColumn(SqliteDataReader reader, string name) =>
        reader.IsDBNull(reader.GetOrdinal(name));

    /// <summary>解析 ISO 8601 往返格式日期（带时区信息的本地时间）。</summary>
    private static DateTime ParseDate(string value) =>
        DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);

    /// <summary>添加参数，自动处理 null → DBNull。</summary>
    private static void AddParam(SqliteCommand cmd, string name, object? value)
    {
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
