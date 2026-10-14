using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using PianAssistant.Services;

namespace PianAssistant.Views;

/// <summary>
/// 把截图输出路径（字符串）转换为低分辨率缩略图 <see cref="BitmapImage"/>。
/// 供列表/画廊网格使用，避免把全分辨率 PNG 完整解码进内存。
/// ConverterParameter 指定 DecodePixelWidth（默认 200，足以覆盖 160×90 与原 120×68）；
/// 传入 0 或负数时按全分辨率解码（画廊大图查看用），且不进入缓存。
/// 转换器持有静态缓存实例（<see cref="ThumbnailImageCache.Default"/>），
/// 相同 (路径, 宽度) 命中缓存直接复用。
/// </summary>
public sealed class ThumbnailConverter : IValueConverter
{
    /// <summary>默认缩略图解码宽度。</summary>
    public const int DefaultDecodePixelWidth = 200;

    public static readonly ThumbnailConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;

        var width = ParseWidth(parameter);
        if (width <= 0)
            return ThumbnailImageCache.DecodeUncached(path);

        return ThumbnailImageCache.Default.Get(path, width);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static int ParseWidth(object? parameter)
    {
        if (parameter is null) return DefaultDecodePixelWidth;
        try
        {
            return System.Convert.ToInt32(parameter, CultureInfo.InvariantCulture);
        }
        catch
        {
            return DefaultDecodePixelWidth;
        }
    }
}
