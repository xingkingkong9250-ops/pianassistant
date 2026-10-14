using System.IO;
using System.Windows.Media.Imaging;

namespace PianAssistant.Services;

/// <summary>
/// 缩略图解码缓存：按 (文件路径, DecodePixelWidth) 缓存已解码的
/// <see cref="BitmapImage"/>，避免列表/画廊把全分辨率图片完整解码进内存。
/// 采用 LRU 淘汰（默认容量 <see cref="DefaultCapacity"/>），超限时移除最久未用项。
/// 解码使用 <see cref="BitmapCacheOption.OnLoad"/>，解码后立即释放文件流，
/// 不持有文件锁；图像解码后冻结（Freeze）以便跨线程共享。
/// </summary>
public sealed class ThumbnailImageCache
{
    /// <summary>默认容量（项数）。</summary>
    public const int DefaultCapacity = 200;

    /// <summary>供 XAML 转换器使用的进程级共享实例。</summary>
    public static ThumbnailImageCache Default { get; } = new(DefaultCapacity);

    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<(string Path, int Width), LinkedListNode<CacheEntry>> _map = new();
    private readonly LinkedList<CacheEntry> _lru = new();

    public ThumbnailImageCache(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), "容量必须大于 0。");
        _capacity = capacity;
    }

    /// <summary>缓存容量上限（项数）。</summary>
    public int Capacity => _capacity;

    /// <summary>当前缓存项数。</summary>
    public int Count
    {
        get { lock (_gate) return _map.Count; }
    }

    /// <summary>
    /// 获取指定路径的缩略图（按 <paramref name="decodePixelWidth"/> 解码）。
    /// 命中直接返回缓存实例；未命中则同步解码并写入缓存。
    /// 文件不存在或解码失败返回 null（调用方按空图处理）。
    /// </summary>
    public BitmapImage? Get(string path, int decodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var key = (path, decodePixelWidth);
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                // 命中：提升为最近使用
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                return existing.Value.Image;
            }
        }

        var image = TryDecode(path, decodePixelWidth);
        if (image is null)
            return null;

        lock (_gate)
        {
            // 双重检查：并发解码期间可能已被其它线程写入
            if (_map.TryGetValue(key, out var raced))
            {
                _lru.Remove(raced);
                _lru.AddFirst(raced);
                return raced.Value.Image;
            }
            var node = _lru.AddFirst(new CacheEntry(key, image));
            _map[key] = node;
            Trim();
        }
        return image;
    }

    /// <summary>清空缓存。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _lru.Clear();
        }
    }

    /// <summary>
    /// 全分辨率解码（不进入缓存）：用于画廊大图查看等需要清晰度的场景。
    /// 仍使用 OnLoad 以立即释放文件流。
    /// </summary>
    internal static BitmapImage? DecodeUncached(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        return TryDecode(path, 0);
    }

    private void Trim()
    {
        // 超限时从 LRU 尾部（最久未用）淘汰
        while (_map.Count > _capacity && _lru.Last is { } last)
        {
            _lru.RemoveLast();
            _map.Remove(last.Value.Key);
        }
    }

    private static BitmapImage? TryDecode(string path, int decodePixelWidth)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            // 避免 WPF 全局 Uri 缓存（同名不同尺寸需各自解码）
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            if (decodePixelWidth > 0)
                image.DecodePixelWidth = decodePixelWidth;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ThumbnailImageCache] 解码失败 {path}: {ex.Message}");
            return null;
        }
    }

    private readonly record struct CacheEntry((string Path, int Width) Key, BitmapImage Image);
}
