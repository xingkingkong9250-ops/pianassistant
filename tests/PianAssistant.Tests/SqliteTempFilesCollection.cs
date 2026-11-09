using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// 使用临时 SQLite 文件的测试集合：串行执行。
/// 这些测试在 Dispose 中调用 <c>SqliteConnection.ClearAllPools()</c> 以释放文件句柄，
/// 该调用会全局清理连接池，与其它类正在打开的连接并发时会偶发
/// <c>ObjectDisposedException: SQLitePCL.sqlite3</c>（v1.15.8 修复门禁偶发失败）。
/// </summary>
[CollectionDefinition(Name)]
public sealed class SqliteTempFilesCollection
{
    public const string Name = "sqlite-temp-files";
}
