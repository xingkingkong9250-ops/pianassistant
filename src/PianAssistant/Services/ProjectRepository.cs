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

    // ==================== 素材会话 CRUD（M7） ====================

    /// <summary>按快速键加载会话（含明细）；不存在返回 null。</summary>
    public MediaSession? LoadSessionByFastKey(string fastKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fastKey);
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM media_sessions WHERE fast_key = @fast_key LIMIT 1";
        AddParam(cmd, "@fast_key", fastKey);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var session = ReadSession(reader);
        reader.Close();
        LoadSessionDetails(conn, session);
        return session;
    }

    /// <summary>按内容哈希加载会话（含明细）；无命中返回 null。</summary>
    public MediaSession? LoadSessionByHash(string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM media_sessions WHERE content_hash = @hash LIMIT 1";
        AddParam(cmd, "@hash", contentHash);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var session = ReadSession(reader);
        reader.Close();
        LoadSessionDetails(conn, session);
        return session;
    }

    /// <summary>
    /// 写入/更新会话（明细整体替换，事务内完成）。
    /// 若 <see cref="MediaSession.ContentHash"/> 已被另一快速键占用，则本行哈希置空
    /// （满足唯一索引；旧的哈希行保留不删除，由业务按 updated_at 合并）。
    /// </summary>
    public void UpsertSession(MediaSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.FastKey);

        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        // 哈希冲突保护：同一内容哈希只允许一行持有（唯一索引），其余置空。
        var effectiveHash = session.ContentHash;
        if (!string.IsNullOrWhiteSpace(effectiveHash))
        {
            using var check = conn.CreateCommand();
            check.Transaction = tx;
            check.CommandText =
                "SELECT 1 FROM media_sessions WHERE content_hash = @hash AND fast_key <> @fk LIMIT 1";
            AddParam(check, "@hash", effectiveHash);
            AddParam(check, "@fk", session.FastKey);
            if (check.ExecuteScalar() is not null)
                effectiveHash = null;
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO media_sessions
                    (fast_key, content_hash, path, size, mtime_utc, last_position_ms, rate,
                     in_point_ms, out_point_ms, updated_at, project_name)
                VALUES
                    (@fast_key, @content_hash, @path, @size, @mtime_utc, @last_position_ms, @rate,
                     @in_point_ms, @out_point_ms, @updated_at, @project_name)
                ON CONFLICT(fast_key) DO UPDATE SET
                    content_hash = excluded.content_hash,
                    path = excluded.path,
                    size = excluded.size,
                    mtime_utc = excluded.mtime_utc,
                    last_position_ms = excluded.last_position_ms,
                    rate = excluded.rate,
                    in_point_ms = excluded.in_point_ms,
                    out_point_ms = excluded.out_point_ms,
                    updated_at = excluded.updated_at,
                    project_name = excluded.project_name
                """;
            AddParam(cmd, "@fast_key", session.FastKey);
            AddParam(cmd, "@content_hash", effectiveHash);
            AddParam(cmd, "@path", session.Path);
            AddParam(cmd, "@size", session.Size);
            AddParam(cmd, "@mtime_utc", session.MtimeUtcTicks);
            AddParam(cmd, "@last_position_ms", session.LastPositionMs);
            AddParam(cmd, "@rate", session.Rate);
            AddParam(cmd, "@in_point_ms", session.InPointMs);
            AddParam(cmd, "@out_point_ms", session.OutPointMs);
            AddParam(cmd, "@updated_at", session.UpdatedAt.ToString("o"));
            AddParam(cmd, "@project_name", session.ProjectName);
            cmd.ExecuteNonQuery();
        }

        // 明细整体替换。
        foreach (var table in new[] { "session_cut_points", "session_screenshots", "session_notes" })
        {
            using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = $"DELETE FROM {table} WHERE session_id = @sid";
            AddParam(del, "@sid", session.FastKey);
            del.ExecuteNonQuery();
        }

        foreach (var ms in session.CutPointsMs)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO session_cut_points (session_id, position_ms) VALUES (@sid, @pos)";
            AddParam(cmd, "@sid", session.FastKey);
            AddParam(cmd, "@pos", ms);
            cmd.ExecuteNonQuery();
        }

        foreach (var s in session.Screenshots)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO session_screenshots (session_id, file_path, position_ms, captured_at, label)
                VALUES (@sid, @file_path, @pos, @captured_at, @label)
                """;
            AddParam(cmd, "@sid", session.FastKey);
            AddParam(cmd, "@file_path", s.FilePath);
            AddParam(cmd, "@pos", s.PositionMs);
            AddParam(cmd, "@captured_at", s.CapturedAt.ToString("o"));
            AddParam(cmd, "@label", s.Label);
            cmd.ExecuteNonQuery();
        }

        foreach (var n in session.Notes)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO session_notes (session_id, position_ms, text, created_at)
                VALUES (@sid, @pos, @text, @created_at)
                """;
            AddParam(cmd, "@sid", session.FastKey);
            AddParam(cmd, "@pos", n.PositionMs);
            AddParam(cmd, "@text", n.Text);
            AddParam(cmd, "@created_at", n.CreatedAt.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>仅更新会话的播放位置与倍速（轻量写入）。</summary>
    public void TouchSessionPosition(string fastKey, long positionMs, double rate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fastKey);
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE media_sessions
            SET last_position_ms = @pos, rate = @rate, updated_at = @updated_at
            WHERE fast_key = @fast_key
            """;
        AddParam(cmd, "@pos", positionMs);
        AddParam(cmd, "@rate", rate);
        AddParam(cmd, "@updated_at", DateTime.Now.ToString("o"));
        AddParam(cmd, "@fast_key", fastKey);
        cmd.ExecuteNonQuery();
    }

    /// <summary>删除会话及其明细。</summary>
    public void DeleteSession(string fastKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fastKey);
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();
        foreach (var (table, column) in new[]
        {
            ("session_cut_points", "session_id"),
            ("session_screenshots", "session_id"),
            ("session_notes", "session_id"),
            ("media_sessions", "fast_key"),
        })
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM {table} WHERE {column} = @id";
            AddParam(cmd, "@id", fastKey);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static MediaSession ReadSession(SqliteDataReader reader)
    {
        return new MediaSession
        {
            FastKey = GetString(reader, "fast_key"),
            ContentHash = IsDBNullColumn(reader, "content_hash") ? null : GetString(reader, "content_hash"),
            Path = GetString(reader, "path"),
            Size = GetInt64(reader, "size"),
            MtimeUtcTicks = GetInt64(reader, "mtime_utc"),
            LastPositionMs = GetInt64(reader, "last_position_ms"),
            Rate = reader.GetDouble(reader.GetOrdinal("rate")),
            InPointMs = IsDBNullColumn(reader, "in_point_ms") ? null : GetInt64(reader, "in_point_ms"),
            OutPointMs = IsDBNullColumn(reader, "out_point_ms") ? null : GetInt64(reader, "out_point_ms"),
            UpdatedAt = ParseDate(GetString(reader, "updated_at")),
            ProjectName = IsDBNullColumn(reader, "project_name") ? null : GetString(reader, "project_name"),
        };
    }

    private static void LoadSessionDetails(SqliteConnection conn, MediaSession session)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT position_ms FROM session_cut_points WHERE session_id = @sid ORDER BY position_ms ASC";
            AddParam(cmd, "@sid", session.FastKey);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                session.CutPointsMs.Add(reader.GetInt64(0));
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT file_path, position_ms, captured_at, label
                FROM session_screenshots WHERE session_id = @sid ORDER BY position_ms ASC
                """;
            AddParam(cmd, "@sid", session.FastKey);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                session.Screenshots.Add(new SessionScreenshot
                {
                    FilePath = reader.GetString(0),
                    PositionMs = reader.GetInt64(1),
                    CapturedAt = ParseDate(reader.GetString(2)),
                    Label = reader.IsDBNull(3) ? null : reader.GetString(3),
                });
            }
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT position_ms, text, created_at
                FROM session_notes WHERE session_id = @sid ORDER BY position_ms ASC
                """;
            AddParam(cmd, "@sid", session.FastKey);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                session.Notes.Add(new SessionNote
                {
                    PositionMs = reader.GetInt64(0),
                    Text = reader.GetString(1),
                    CreatedAt = ParseDate(reader.GetString(2)),
                });
            }
        }
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
