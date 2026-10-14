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
    /// <summary>当前 schema 版本号。</summary>
    public const int CurrentSchemaVersion = 1;

    private readonly string _dbPath;

    /// <summary>数据库文件完整路径。</summary>
    public string DatabasePath => _dbPath;

    public ProjectDatabase(string? dbPath = null)
    {
        _dbPath = dbPath ?? DefaultDbPath();
        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>计算默认数据库路径（遵循 AppSettingsService 的数据根约定）。</summary>
    private static string DefaultDbPath()
    {
        var envOverride = Environment.GetEnvironmentVariable("PIAN_ASSISTANT_DATA_ROOT");
        var root = string.IsNullOrWhiteSpace(envOverride)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Qianlan_App", "PianAssistant")
            : envOverride!;
        return Path.Combine(root, "pianassistant.db");
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
    /// 初始化数据库：创建所有表并写入 schema_version=1。
    /// 幂等——重复调用不会重建已有表。
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
        versionCmd.Parameters.AddWithValue("@version", CurrentSchemaVersion);
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
    /// 检查当前 schema 版本并执行增量迁移。
    /// v1 为初始版本，无需迁移；后续新增版本时在此追加迁移逻辑。
    /// </summary>
    public void MigrateIfNeeded()
    {
        var current = GetSchemaVersion();
        if (current >= CurrentSchemaVersion)
            return;

        // 占位：后续版本迁移在此按 current+1 → CurrentSchemaVersion 逐步执行。
        // 示例（v2 迁移）：
        // if (current < 2)
        // {
        //     using var conn = CreateConnection();
        //     using var cmd = conn.CreateCommand();
        //     cmd.CommandText = "ALTER TABLE projects ADD COLUMN extra TEXT";
        //     cmd.ExecuteNonQuery();
        //     RecordMigration(conn, 2);
        // }
    }

    /// <summary>在迁移完成后记录版本号。</summary>
    internal void RecordMigration(SqliteConnection conn, int version)
    {
        using var cmd = conn.CreateCommand();
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
