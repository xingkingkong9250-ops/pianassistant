using System.IO;
using Microsoft.Data.Sqlite;

namespace PianAssistant.Services;

/// <summary>
/// SQLite 数据库封装：负责创建 schema、初始化与版本迁移。
/// 数据库路径：<c>%APPDATA%\Qianlan_App\PianAssistant\pianassistant.db</c>
/// （支持 <c>PIAN_ASSISTANT_DATA_ROOT</c> 环境变量覆盖，与 AppSettingsService 一致）。
/// </summary>
public sealed class ProjectDatabase
{
    /// <summary>当前 schema 版本号（M7 起为 2）。</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>v1 初始版本号。</summary>
    public const int BaseSchemaVersion = 1;

    private readonly string _dbPath;

    /// <summary>数据库文件完整路径。</summary>
    public string DatabasePath => _dbPath;

    public ProjectDatabase(string? dbPath = null)
    {
        // 默认路径统一由 DataRootResolver 解析（env > config.DataRoot > 引导目录）。
        _dbPath = dbPath ?? DataRootResolver.DatabasePath;
        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>创建并打开一个 SQLite 连接（调用方负责 Dispose）。</summary>
    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    /// <summary>
    /// 初始化数据库并在打开失败（文件损坏）时自愈：
    /// 把损坏的库重命名为 <c>*.corrupt</c>（保留数据、不删除）后重建空库。
    /// 返回结果供启动层记录日志 / 友好提示。绝不静默丢弃原文件。
    /// </summary>
    public DatabaseOpenResult EnsureInitialized()
    {
        try
        {
            Initialize();
            MigrateIfNeeded();
            return new DatabaseOpenResult(true, false, null);
        }
        catch (SqliteException ex)
        {
            AppLog.Error($"[ProjectDatabase] 打开数据库失败：{_dbPath}", ex);
            var quarantined = QuarantineCorruptDatabase();
            try
            {
                Initialize();
                MigrateIfNeeded();
                var detail = quarantined is null
                    ? "已重建空库。"
                    : $"原库已保留为 {quarantined}，并重建了空库。";
                return new DatabaseOpenResult(true, true, $"数据库文件损坏，{detail}");
            }
            catch (SqliteException ex2)
            {
                AppLog.Error("[ProjectDatabase] 重建数据库仍失败", ex2);
                return new DatabaseOpenResult(false, true,
                    "数据库已损坏且无法重建，项目持久化功能暂不可用。");
            }
        }
    }

    /// <summary>把损坏的库文件重命名为 <c>*.corrupt</c>（保留数据），返回新路径。</summary>
    private string? QuarantineCorruptDatabase()
    {
        try
        {
            if (!File.Exists(_dbPath)) return null;
            // 释放连接池以解除文件锁
            SqliteConnection.ClearAllPools();
            var target = _dbPath + ".corrupt";
            if (File.Exists(target))
                target = $"{_dbPath}.corrupt.{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(_dbPath, target, overwrite: true);
            // 清理可能残留的 WAL/SHM 旁文件
            foreach (var sidecar in new[] { _dbPath + "-wal", _dbPath + "-shm" })
            {
                if (File.Exists(sidecar))
                    File.Delete(sidecar);
            }
            return target;
        }
        catch (Exception ex)
        {
            AppLog.Error("[ProjectDatabase] 隔离损坏数据库失败", ex);
            return null;
        }
    }

    /// <summary>
    /// 初始化数据库：创建 v1 基础表并写入 schema_version=1。
    /// 幂等——重复调用不会重建已有表。v2 表由 <see cref="MigrateIfNeeded"/> 增量迁移创建。
    /// </summary>
    public void Initialize()
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER PRIMARY KEY,
                applied_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS projects (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                source_path TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS screenshots (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                source_path TEXT NOT NULL,
                output_path TEXT NOT NULL,
                position_ms INTEGER NOT NULL,
                captured_at TEXT NOT NULL,
                label TEXT
            );

            CREATE TABLE IF NOT EXISTS clips (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                source_path TEXT NOT NULL,
                output_path TEXT,
                in_point_ms INTEGER NOT NULL,
                out_point_ms INTEGER,
                label TEXT,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS notes (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                source_path TEXT NOT NULL,
                position_ms INTEGER NOT NULL,
                text TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_screenshots_project ON screenshots(project_id);
            CREATE INDEX IF NOT EXISTS idx_clips_project ON clips(project_id);
            CREATE INDEX IF NOT EXISTS idx_notes_project ON notes(project_id);
            """;
        cmd.ExecuteNonQuery();

        // 写入 schema_version=1（幂等）。
        using var versionCmd = conn.CreateCommand();
        versionCmd.CommandText =
            "INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (@version, @applied_at)";
        versionCmd.Parameters.AddWithValue("@version", BaseSchemaVersion);
        versionCmd.Parameters.AddWithValue("@applied_at", DateTime.Now.ToString("o"));
        versionCmd.ExecuteNonQuery();
    }

    /// <summary>读取当前已应用的 schema 版本号；无记录时返回 0。</summary>
    public int GetSchemaVersion()
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(version) FROM schema_version";
        var result = cmd.ExecuteScalar();
        if (result is null || result == DBNull.Value)
            return 0;
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// 检查当前 schema 版本并执行增量迁移（幂等）。
    /// v1 → v2：新增素材会话表（media_sessions 及明细），全部为
    /// <c>CREATE TABLE IF NOT EXISTS</c>，不修改/删除任何既有数据，老用户升级不丢数据。
    /// </summary>
    public void MigrateIfNeeded()
    {
        var current = GetSchemaVersion();
        if (current >= CurrentSchemaVersion)
            return;

        if (current < 2)
            MigrateToV2();
    }

    /// <summary>v1 → v2 迁移：新增 media_sessions 会话表与明细表（幂等，不动旧数据）。</summary>
    private void MigrateToV2()
    {
        using var conn = CreateConnection();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS media_sessions (
                    fast_key TEXT PRIMARY KEY,
                    content_hash TEXT,
                    path TEXT NOT NULL,
                    size INTEGER NOT NULL,
                    mtime_utc INTEGER NOT NULL,
                    last_position_ms INTEGER NOT NULL DEFAULT 0,
                    rate REAL NOT NULL DEFAULT 1.0,
                    in_point_ms INTEGER,
                    out_point_ms INTEGER,
                    updated_at TEXT NOT NULL,
                    project_name TEXT
                );

                CREATE UNIQUE INDEX IF NOT EXISTS idx_media_sessions_hash
                    ON media_sessions(content_hash) WHERE content_hash IS NOT NULL;

                CREATE TABLE IF NOT EXISTS session_cut_points (
                    session_id TEXT NOT NULL,
                    position_ms INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS session_screenshots (
                    session_id TEXT NOT NULL,
                    file_path TEXT NOT NULL,
                    position_ms INTEGER NOT NULL,
                    captured_at TEXT NOT NULL,
                    label TEXT
                );

                CREATE TABLE IF NOT EXISTS session_notes (
                    session_id TEXT NOT NULL,
                    position_ms INTEGER NOT NULL,
                    text TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_session_cut_points ON session_cut_points(session_id);
                CREATE INDEX IF NOT EXISTS idx_session_screenshots ON session_screenshots(session_id);
                CREATE INDEX IF NOT EXISTS idx_session_notes ON session_notes(session_id);
                """;
            cmd.ExecuteNonQuery();
        }
        RecordMigration(conn, 2, tx);
        tx.Commit();
    }

    /// <summary>在迁移完成后记录版本号。</summary>
    internal void RecordMigration(SqliteConnection conn, int version) =>
        RecordMigration(conn, version, null);

    /// <summary>在迁移完成后记录版本号（可复用事务）。</summary>
    internal void RecordMigration(SqliteConnection conn, int version, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT OR REPLACE INTO schema_version (version, applied_at) VALUES (@version, @applied_at)";
        cmd.Parameters.AddWithValue("@version", version);
        cmd.Parameters.AddWithValue("@applied_at", DateTime.Now.ToString("o"));
        cmd.ExecuteNonQuery();
    }
}

/// <summary>
/// 数据库打开结果：是否成功、是否发生损坏自愈、以及可展示给用户的说明。
/// </summary>
public readonly record struct DatabaseOpenResult(bool Success, bool Recovered, string? Message);
