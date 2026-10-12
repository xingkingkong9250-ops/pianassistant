namespace PianAssistant.Models;

/// <summary>
/// 单条批量指令的执行结果。
/// </summary>
public sealed class BatchResult
{
    public bool Success { get; private init; }
    public string? OutputPath { get; private init; }
    public string? Error { get; private init; }
    public TimeSpan Elapsed { get; private init; }

    /// <summary>构造成功结果（OutputPath 为截图/片段输出路径；标记/裁切点等跳过型为 null）。</summary>
    public static BatchResult Ok(string? outputPath = null, TimeSpan elapsed = default) => new()
    {
        Success = true,
        OutputPath = outputPath,
        Elapsed = elapsed,
    };

    /// <summary>构造失败结果。</summary>
    public static BatchResult Fail(string error, TimeSpan elapsed = default) => new()
    {
        Success = false,
        Error = error,
        Elapsed = elapsed,
    };

    // --- UI 绑定辅助属性 ---

    /// <summary>状态图标（✔ / ✘）。</summary>
    public string StatusIcon => Success ? "✔" : "✘";

    /// <summary>耗时显示文本。</summary>
    public string ElapsedText => $"{Elapsed.TotalMilliseconds:0} ms";

    /// <summary>结果明细：成功显示输出路径（跳过型显示「已应用」），失败显示原因。</summary>
    public string DetailText => Success
        ? (OutputPath ?? "已应用")
        : (Error ?? "失败");
}
