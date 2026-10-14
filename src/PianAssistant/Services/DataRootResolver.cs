using System.IO;

namespace PianAssistant.Services;

/// <summary>
/// 数据根路径解析的唯一入口（M7 数据便携化）。
/// 解析优先级：环境变量 <c>PIAN_ASSISTANT_DATA_ROOT</c>（测试隔离用）
/// &gt; config.json 中的 <c>DataRoot</c> &gt; 默认引导目录
/// （<c>%APPDATA%\Qianlan_App\PianAssistant\</c>）。
/// 注意：config.json 本身固定放在引导目录，绝不随数据根迁移。
/// 所有需要落盘的服务一律从此解析路径，不得各自拼路径。
/// </summary>
public static class DataRootResolver
{
    private const string EnvVarName = "PIAN_ASSISTANT_DATA_ROOT";

    private static readonly string DefaultBootstrapRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Qianlan_App", "PianAssistant");

    /// <summary>固定的引导目录（config.json 所在）；受环境变量覆盖。</summary>
    public static string BootstrapRoot
    {
        get
        {
            var env = Environment.GetEnvironmentVariable(EnvVarName);
            return string.IsNullOrWhiteSpace(env) ? DefaultBootstrapRoot : env!;
        }
    }

    /// <summary>config.json 路径（固定引导目录，不随数据根迁移）。</summary>
    public static string ConfigPath => Path.Combine(BootstrapRoot, "config.json");

    /// <summary>
    /// 解析数据根：环境变量 &gt; config.DataRoot &gt; 默认引导目录。
    /// </summary>
    /// <param name="configDataRoot">config.json 中的 DataRoot；空白视为未设置。</param>
    /// <param name="bootstrapRootOverride">测试注入的引导根覆盖（等价于环境变量的优先作用）。</param>
    public static string ResolveDataRoot(string? configDataRoot = null, string? bootstrapRootOverride = null)
    {
        var env = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(env)) return env!;
        if (!string.IsNullOrWhiteSpace(configDataRoot)) return configDataRoot!;
        if (!string.IsNullOrWhiteSpace(bootstrapRootOverride)) return bootstrapRootOverride!;
        return DefaultBootstrapRoot;
    }

    /// <summary>默认（无 config 上下文）数据根。</summary>
    public static string DataRoot => ResolveDataRoot();

    /// <summary>数据库文件路径（数据根下 pianassistant.db）。</summary>
    public static string DatabasePath => Path.Combine(DataRoot, "pianassistant.db");

    /// <summary>截图输出目录（数据根下 screenshots）。</summary>
    public static string ScreenshotsDir => Path.Combine(DataRoot, "screenshots");

    /// <summary>片段输出目录（数据根下 clips）。</summary>
    public static string ClipsDir => Path.Combine(DataRoot, "clips");

    /// <summary>日志目录（数据根下 logs）。</summary>
    public static string LogsDir => Path.Combine(DataRoot, "logs");
}
