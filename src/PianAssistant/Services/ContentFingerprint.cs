using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PianAssistant.Services;

/// <summary>
/// 素材身份指纹（M7 两级键）：
/// 快速键 = 路径 + 文件大小 + 最后修改时间（UTC ticks），用于打开瞬间立即定位；
/// 规范键 = 内容 SHA-256（流式 1MB 分块），跨设备/改名后仍能识别同一素材。
/// 任何文件不存在/读取失败都返回 null，绝不抛出（避免拖垮打开流程）。
/// </summary>
public static class ContentFingerprint
{
    /// <summary>流式哈希分块大小（1MB）。</summary>
    private const int ChunkSize = 1024 * 1024;

    /// <summary>
    /// 计算快速键：<c>路径|大小|mtimeUtcTicks</c>。
    /// mtime 采用 UTC ticks，保证跨时区/夏令时稳定。文件缺失返回 null。
    /// </summary>
    public static string? ComputeFastKey(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return null;
            return $"{path}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 计算内容哈希（SHA-256 十六进制小写）。流式 1MB 分块读取，
    /// <see cref="CancellationToken"/> 贯穿；文件缺失/读失败返回 null。
    /// </summary>
    public static async Task<string?> ComputeContentHashAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                ChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var sha = SHA256.Create();
            var buffer = new byte[ChunkSize];
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, ChunkSize), ct).ConfigureAwait(false)) > 0)
            {
                sha.TransformBlock(buffer, 0, read, null, 0);
            }
            sha.TransformFinalBlock(buffer, 0, 0);
            ct.ThrowIfCancellationRequested();
            return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
