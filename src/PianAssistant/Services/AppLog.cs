using System.IO;
using System.Text;

namespace PianAssistant.Services;

/// <summary>
/// 轻量文件日志：写入 <c>%APPDATA%\Qianlan_App\PianAssistant\logs\</c>
/// （遵循 <see cref="AppSettingsService.GetEffectiveRoot"/> 的数据根约定，
/// 因此同样受 <c>PIAN_ASSISTANT_DATA_ROOT</c> 覆盖）。
/// 任何写入失败都被吞掉——日志本身绝不能拖垮应用。
/// </summary>
internal static class AppLog
{
    private static readonly object Gate = new();

    /// <summary>日志目录（绝对路径）。</summary>
    public static string LogDirectory { get; } =
        Path.Combine(AppSettingsService.GetEffectiveRoot(), "logs");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string context, Exception ex) =>
        Write("ERROR", $"{context}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var path = Path.Combine(LogDirectory, $"pianassistant_{DateTime.Now:yyyyMMdd}.log");
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }
}
