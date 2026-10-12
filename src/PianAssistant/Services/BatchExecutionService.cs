using System.Diagnostics;
using System.IO;
using PianAssistant.Helpers;
using PianAssistant.Models;

namespace PianAssistant.Services;

/// <summary>
/// 批量指令执行器：逐条执行截图（ffmpeg 单帧截取）与片段（ffmpeg 截段）指令。
/// 标记与裁切点指令由 ViewModel 层直接分发到笔记/播放器，执行器跳过并标成功。
/// 单条失败不中断后续条目；取消（<see cref="OperationCanceledException"/>）向上抛出。
/// </summary>
public sealed class BatchExecutionService
{
    private readonly FfmpegRunner _ffmpeg;
    private readonly ClipExtractionService _clips;
    private readonly AppSettingsService _settings;

    public BatchExecutionService(FfmpegRunner ffmpeg, ClipExtractionService clips, AppSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(ffmpeg);
        ArgumentNullException.ThrowIfNull(clips);
        ArgumentNullException.ThrowIfNull(settings);
        _ffmpeg = ffmpeg;
        _clips = clips;
        _settings = settings;
    }

    /// <summary>
    /// 逐条执行指令。每条完成后回调 <paramref name="onItemDone"/>（已完成数, 总数, 指令, 结果），
    /// 含被跳过的标记/裁切点与解析失败的指令。ffmpeg 工作经 <see cref="Task.Run"/> 避免阻塞。
    /// </summary>
    public async Task ExecuteAsync(
        IReadOnlyList<BatchInstruction> instructions,
        string videoPath,
        CancellationToken ct,
        Action<int, int, BatchInstruction, BatchResult> onItemDone)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        ArgumentNullException.ThrowIfNull(onItemDone);

        for (int i = 0; i < instructions.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var instruction = instructions[i];
            BatchResult result;
            if (!instruction.IsValid)
            {
                // 解析失败的指令：直接标失败（正常流程 VM 只传有效指令，此处兜底）
                result = BatchResult.Fail(instruction.Error ?? "解析错误");
            }
            else
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    switch (instruction.Type)
                    {
                        case BatchActionKind.Marker:
                        case BatchActionKind.CutPoint:
                            // 由 VM 层分发到笔记/播放器，执行器无需动作
                            result = BatchResult.Ok();
                            break;
                        case BatchActionKind.Screenshot:
                            var path = await Task.Run(() => CaptureScreenshot(videoPath, instruction), ct);
                            sw.Stop();
                            result = BatchResult.Ok(path, sw.Elapsed);
                            break;
                        case BatchActionKind.Clip:
                            var range = new ClipRange(videoPath, instruction.Start, instruction.End, instruction.Label);
                            var clipPath = await Task.Run(
                                () => _clips.Extract(range, false, ct), ct);
                            sw.Stop();
                            result = BatchResult.Ok(clipPath, sw.Elapsed);
                            break;
                        default:
                            result = BatchResult.Fail("未知指令类型");
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw; // 取消：中止整个批量任务
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    result = BatchResult.Fail(ex.Message, sw.Elapsed);
                }
            }
            onItemDone(i + 1, instructions.Count, instruction, result);
        }
    }

    /// <summary>
    /// 用 ffmpeg 截取单帧：输出目录按设置（截图目录 + 日期/项目子目录），
    /// 文件名为「时间码_[标签].扩展名」。
    /// </summary>
    private string CaptureScreenshot(string videoPath, BatchInstruction instruction)
    {
        var outputDir = OutputDirectoryResolver.Resolve(
            _settings.ScreenshotsDirectory, _settings.OrganizeByDate,
            _settings.ProjectSubfolderName, DateTime.Now);
        Directory.CreateDirectory(outputDir);

        var stamp = TimeCodeFormatter.ToFileNameFragment(instruction.Start);
        var label = string.IsNullOrWhiteSpace(instruction.Label)
            ? string.Empty
            : "_" + SanitizeFileName(instruction.Label);
        var outputPath = Path.Combine(outputDir, $"{stamp}{label}.{_settings.SnapshotFormat}");
        outputPath = MakeUnique(outputPath);

        _ffmpeg.CaptureSingleFrame(videoPath, instruction.Start, outputPath);
        return outputPath;
    }

    /// <summary>清理文件名非法字符（标签用于输出文件名）。</summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name.Trim())
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        return sb.ToString();
    }

    /// <summary>输出路径重名时追加序号（与 ScreenshotService 同规则）。</summary>
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
