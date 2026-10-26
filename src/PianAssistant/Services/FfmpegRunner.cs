using System.Diagnostics;
using System.IO;
using System.Text;

namespace PianAssistant.Services;

/// <summary>
/// Wraps ffmpeg.exe / ffprobe.exe invocations as a child process.
/// Stdout is parsed for progress; stderr is captured for diagnostics.
/// </summary>
public sealed class FfmpegRunner
{
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;

    public FfmpegRunner(string ffmpegPath, string ffprobePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(ffprobePath);
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath;
    }

    /// <summary>
    /// Creates a FfmpegRunner that resolves ffmpeg.exe / ffprobe.exe from
    /// the bundled Tools/ffmpeg/ directory next to AppContext.BaseDirectory.
    /// Throws FileNotFoundException if the binaries are missing (first
    /// clone needs tools/download-ffmpeg.ps1).
    /// </summary>
    public static FfmpegRunner FromBundledTools()
    {
        var root = AppContext.BaseDirectory;
        var ffmpeg = Path.Combine(root, "tools", "ffmpeg", "ffmpeg.exe");
        var ffprobe = Path.Combine(root, "tools", "ffmpeg", "ffprobe.exe");
        if (!File.Exists(ffmpeg))
            throw new FileNotFoundException(
                "ffmpeg.exe not found at " + ffmpeg +
                ". Run tools/download-ffmpeg.ps1 first.", ffmpeg);
        if (!File.Exists(ffprobe))
            throw new FileNotFoundException(
                "ffprobe.exe not found at " + ffprobe +
                ". Run tools/download-ffmpeg.ps1 first.", ffprobe);
        return new FfmpegRunner(ffmpeg, ffprobe);
    }

    public string FfmpegPath => _ffmpegPath;
    public string FfprobePath => _ffprobePath;

    /// <summary>
    /// 运行前校验 ffmpeg.exe 是否存在：缺失时抛出带中文提示与脚本指引的
    /// <see cref="FileNotFoundException"/>，避免静默失败或裸 Win32Exception。
    /// </summary>
    private void EnsureBinariesPresent()
    {
        if (!File.Exists(_ffmpegPath))
            throw new FileNotFoundException(
                "未找到 ffmpeg.exe，请运行 tools\\download-ffmpeg.ps1 下载后再试。", _ffmpegPath);
    }

    /// <summary>
    /// Capture a single still frame at the given position using ffmpeg.
    /// Used as a fallback when LibVLC TakeSnapshot fails.
    /// </summary>
    /// <param name="scaleFilter">可选缩放滤镜（导出分辨率预设）；null 保持源分辨率。</param>
    public void CaptureSingleFrame(string source, TimeSpan position, string outputPath,
        string? scaleFilter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var args = new StringBuilder();
        args.Append("-ss ").Append(position.TotalSeconds.ToString("0.###")).Append(' ');
        args.Append("-i \"").Append(source).Append("\" ");
        args.Append("-frames:v 1 ");
        AppendScaleFilter(args, scaleFilter);
        var ext = Path.GetExtension(outputPath).ToLowerInvariant();
        args.Append(ext switch
        {
            ".png" => "-compression_level 6 ",
            ".jpg" or ".jpeg" => "-q:v 2 ",
            _ => string.Empty,
        });
        args.Append('"').Append(outputPath).Append('"');
        var (exit, _) = Run(args.ToString(), CancellationToken.None);
        if (exit != 0)
            throw new InvalidOperationException($"ffmpeg single-frame capture failed (exit {exit}).");
    }

    /// <summary>
    /// Extract a clip using stream copy (-c copy, fast, keyframe-aligned).
    /// </summary>
    /// <param name="source">Input video path.</param>
    /// <param name="inPoint">Start time.</param>
    /// <param name="outPoint">End time (exclusive).</param>
    /// <param name="outputPath">Output file path (.mp4 recommended).</param>
    /// <param name="cancellationToken">Cancellation token; kills the child process.</param>
    /// <param name="onProgress">Optional progress callback receiving a 0..1 ratio.</param>
    public void ExtractClipStreamCopy(
        string source,
        TimeSpan inPoint,
        TimeSpan outPoint,
        string outputPath,
        CancellationToken cancellationToken,
        Action<double>? onProgress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (outPoint <= inPoint)
            throw new ArgumentException("outPoint must be after inPoint.", nameof(outPoint));

        var durationMs = (outPoint - inPoint).TotalMilliseconds;
        // 关键：输出长度用输出选项 -t（时长）而非输入选项 -to（绝对时间戳）。
        // 输入级 -ss 会重置输入时间轴，-to 放在 -i 前会相对 seek 后的起点解释，
        // 导致导出片段时长与软件内片段起止对不上（v1.2.2 修复）。
        var args = new StringBuilder();
        args.Append("-ss ").Append(inPoint.TotalSeconds.ToString("0.###")).Append(' ');
        args.Append("-i \"").Append(source).Append("\" ");
        args.Append("-t ").Append(TimeSpan.FromMilliseconds(durationMs).TotalSeconds.ToString("0.###")).Append(' ');
        args.Append("-c copy -avoid_negative_ts make_zero ");
        args.Append('"').Append(outputPath).Append('"');
        RunWithProgress(args.ToString(), durationMs, cancellationToken, onProgress);
    }

    /// <summary>
    /// Extract a clip with re-encoding for millisecond precision.
    /// </summary>
    /// <param name="scaleFilter">可选缩放滤镜（导出分辨率预设）；null 保持源分辨率。</param>
    public void ExtractClipReencode(
        string source,
        TimeSpan inPoint,
        TimeSpan outPoint,
        string outputPath,
        CancellationToken cancellationToken,
        Action<double>? onProgress = null,
        string? scaleFilter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (outPoint <= inPoint)
            throw new ArgumentException("outPoint must be after inPoint.", nameof(outPoint));

        var durationMs = (outPoint - inPoint).TotalMilliseconds;
        // 输入级 -ss（转码时为精确 seek：解码丢帧到目标）+ 输出选项 -t 控制时长，
        // 与流复制路径同一时间基准（v1.2.2：替换 -i 前的 -to，修起止偏差）
        var args = new StringBuilder();
        args.Append("-ss ").Append(inPoint.TotalSeconds.ToString("0.###")).Append(' ');
        args.Append("-i \"").Append(source).Append("\" ");
        args.Append("-t ").Append(TimeSpan.FromMilliseconds(durationMs).TotalSeconds.ToString("0.###")).Append(' ');
        args.Append("-c:v libx264 -preset fast -crf 18 -pix_fmt yuv420p ");
        AppendScaleFilter(args, scaleFilter);
        args.Append("-c:a aac -b:a 192k ");
        // mp4/mov 打开 faststart：便于边下边播；mkv 不支持该选项
        var ext = Path.GetExtension(outputPath).ToLowerInvariant();
        if (ext is ".mp4" or ".mov")
            args.Append("-movflags +faststart ");
        args.Append('"').Append(outputPath).Append('"');
        RunWithProgress(args.ToString(), durationMs, cancellationToken, onProgress);
    }

    /// <summary>
    /// 生成代理媒体（v1.11.0）：按档位降分辨率 + 限码率重编码。
    /// 关键点：<c>-fps_mode passthrough</c> 保持帧率直通，代理与源的时间码逐帧对齐，
    /// 裁切点/截图/片段导出均可直接复用源时间码。
    /// v1.12.0 起支持分段并行：传入 <paramref name="chunkStart"/>/<paramref name="chunkDuration"/>
    /// 后只转该时间段（输入级 -ss 精确 seek + 输出 -t），供并行分段转码使用。
    /// </summary>
    /// <param name="useHardwareEncoder">true 用 h264_nvenc（快且 CPU 占用低）；false 用 libx264。</param>
    /// <param name="totalDurationMs">进度折算用的总时长（分段时为该分段时长）。</param>
    /// <param name="hardwareDecode">true 时加 <c>-hwaccel cuda</c> 用 NVDEC 硬解（HEVC 收益大；失败由上层回退）。</param>
    public void GenerateProxy(
        string source,
        string outputPath,
        PianAssistant.Models.ProxyPreset preset,
        bool useHardwareEncoder,
        double totalDurationMs,
        CancellationToken cancellationToken,
        Action<double>? onProgress = null,
        TimeSpan? chunkStart = null,
        TimeSpan? chunkDuration = null,
        bool hardwareDecode = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(preset);

        var kbps = preset.VideoBitrateKbps;
        var args = new StringBuilder();
        if (hardwareDecode) args.Append("-hwaccel cuda ");
        if (chunkStart is { } start && chunkDuration is { } dur)
        {
            // 输入级 -ss（转码时为精确 seek：解码丢帧到目标）+ 输出 -t 控制分段时长，
            // 与片段导出同一时间基准（v1.2.2 的教训：长度用输出选项 -t）
            args.Append("-ss ").Append(start.TotalSeconds.ToString("0.###")).Append(' ');
            args.Append("-t ").Append(dur.TotalSeconds.ToString("0.###")).Append(' ');
        }
        args.Append("-i \"").Append(source).Append("\" ");
        if (useHardwareEncoder)
        {
            args.Append("-c:v h264_nvenc -preset p1 -rc vbr -cq 28 ");
            args.Append("-b:v ").Append(kbps).Append("k -maxrate ").Append(kbps * 2).Append("k ");
        }
        else
        {
            args.Append("-c:v libx264 -preset veryfast ");
            args.Append("-b:v ").Append(kbps).Append("k -maxrate ").Append(kbps * 2)
                .Append("k -bufsize ").Append(kbps * 4).Append("k ");
        }
        // 等比缩放并按 2 对齐（保证 yuv420p 合法），不放大
        args.Append("-vf \"scale=").Append(preset.Width).Append(':').Append(preset.Height)
            .Append(":force_original_aspect_ratio=decrease:force_divisible_by=2\" ");
        // 关键帧收紧到 1s（seek 响应更快）+ 帧率直通（时间码对齐）
        args.Append("-g 25 -fps_mode passthrough -pix_fmt yuv420p ");
        args.Append("-c:a aac -b:a 96k ");
        args.Append("-movflags +faststart ");
        args.Append('"').Append(outputPath).Append('"');
        RunWithProgressParsing(args.ToString(), totalDurationMs, cancellationToken, onProgress);
    }

    /// <summary>
    /// 运行 ffprobe 并返回 stdout（失败返回空串，绝不抛异常）。
    /// 供代理媒体的分辨率/时长探测使用：探测失败时上游按「无需代理」处理。
    /// </summary>
    public string RunFfprobe(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffprobePath,
                Arguments = arguments,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using var process = new Process { StartInfo = psi };
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return string.Empty;
            }
            var stdout = stdoutTask.GetAwaiter().GetResult();
            stderrTask.GetAwaiter().GetResult();
            return process.ExitCode == 0 ? stdout : string.Empty;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FfmpegRunner] ffprobe failed: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>追加 -vf 缩放滤镜（空值不追加）。</summary>
    private static void AppendScaleFilter(StringBuilder args, string? scaleFilter)
    {
        if (string.IsNullOrWhiteSpace(scaleFilter)) return;
        args.Append("-vf \"").Append(scaleFilter).Append("\" ");
    }

    /// <summary>
    /// 使用 concat demuxer 无损拼接已导出的片段文件。
    /// listPath 为 UTF-8（无 BOM）清单文件，每行 file '路径'。
    /// totalDurationMs 用于把 stderr 的 time= 进度折算到 0..1。
    /// </summary>
    public void ConcatFiles(
        string listPath,
        string outputPath,
        double totalDurationMs,
        CancellationToken cancellationToken,
        Action<double>? onProgress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var args = new StringBuilder();
        args.Append("-f concat -safe 0 -i \"").Append(listPath).Append("\" ");
        args.Append("-c copy ");
        args.Append('"').Append(outputPath).Append('"');
        RunWithProgressParsing(args.ToString(), totalDurationMs, cancellationToken, onProgress);
    }

    /// <summary>
    /// 运行 ffmpeg 并逐行解析 stderr 的 time= 进度（取每个数据块中最后一次
    /// 状态行，避免 \r 分隔的状态更新被合并时进度停滞）。
    /// </summary>
    private void RunWithProgressParsing(
        string arguments,
        double totalDurationMs,
        CancellationToken cancellationToken,
        Action<double>? onProgress)
    {
        EnsureBinariesPresent();
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var process = new Process { StartInfo = psi };
        process.Start();
        // 后台线程逐行读取 stderr 并解析进度，避免缓冲区满导致死锁
        var stderrTask = Task.Run(() =>
        {
            string? line;
            while ((line = process.StandardError.ReadLine()) is not null)
            {
                if (onProgress is null || totalDurationMs <= 0) continue;
                // \r 分隔的状态行会被合并为一行，取最后一个 time= 才是最新进度
                var idx = line.LastIndexOf("time=", StringComparison.Ordinal);
                if (idx >= 0 && ParseFfmpegTimeMs(line[idx..], out var ms) && ms > 0)
                    onProgress(Math.Min(1.0, ms / totalDurationMs));
            }
        });
        while (!process.HasExited)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                cancellationToken.ThrowIfCancellationRequested();
            }
            System.Threading.Thread.Sleep(100);
        }
        stderrTask.GetAwaiter().GetResult(); // 等待 stderr 排空
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"ffmpeg concat failed (exit {process.ExitCode}).");
    }

    private void RunWithProgress(
        string arguments,
        double totalDurationMs,
        CancellationToken cancellationToken,
        Action<double>? onProgress)
    {
        EnsureBinariesPresent();
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var process = new Process { StartInfo = psi };
        process.Start();
        // 异步读取全部 stderr，避免缓冲区满导致死锁
        var stderrTask = process.StandardError.ReadToEndAsync();
        while (!process.HasExited)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                cancellationToken.ThrowIfCancellationRequested();
            }
            System.Threading.Thread.Sleep(100);
        }
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"ffmpeg failed (exit {process.ExitCode}). stderr: {stderr}");
    }

    private (int exitCode, string stderr) Run(string arguments, CancellationToken cancellationToken)
    {
        EnsureBinariesPresent();
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var process = new Process { StartInfo = psi };
        process.Start();
        // 异步读取 stdout+stderr，避免缓冲区满导致死锁
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        while (!process.HasExited)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                cancellationToken.ThrowIfCancellationRequested();
            }
            System.Threading.Thread.Sleep(20);
        }
        var stderr = stderrTask.GetAwaiter().GetResult();
        stdoutTask.GetAwaiter().GetResult(); // drain stdout
        return (process.ExitCode, stderr);
    }

    private static string? ReadLine(System.IO.StreamReader reader)
    {
        try { return reader.ReadLine(); }
        catch { return null; }
    }

    /// <summary>
    /// Parses "time=HH:MM:SS.ff" from a stderr line; returns false on no match.
    /// </summary>
    public static bool ParseFfmpegTimeMs(string line, out double totalMs)
    {
        totalMs = 0;
        var idx = line.IndexOf("time=", StringComparison.Ordinal);
        if (idx < 0) return false;
        idx += "time=".Length;
        var end = idx;
        while (end < line.Length && (char.IsDigit(line[end]) || line[end] is ':' or '.' or '-')) end++;
        var token = line.Substring(idx, end - idx);
        // HH:MM:SS.ff or SS.ff
        if (!TimeSpan.TryParse(token, out var ts)) return false;
        totalMs = ts.TotalMilliseconds;
        return true;
    }
}
