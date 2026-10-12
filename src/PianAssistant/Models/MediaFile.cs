using System.IO;

namespace PianAssistant.Models;

/// <summary>
/// A video file opened in the player. POCO; no INPC.
/// </summary>
public sealed class MediaFile
{
    public string Path { get; }
    public string FileName { get; }
    public long SizeBytes { get; }
    public TimeSpan Duration { get; set; }
    public double Fps { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public MediaFile(string path, long? sizeBytes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        SizeBytes = sizeBytes ?? TryGetFileSize(path);
    }

    private static long TryGetFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }
}
