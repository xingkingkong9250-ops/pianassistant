using System.Globalization;
using System.Windows;
using System.Windows.Data;
using PianAssistant.ViewModels;

namespace PianAssistant.Views;

/// <summary>
/// Converts a SidebarPanel enum value to Visibility.
/// Pass the expected panel name as ConverterParameter (e.g. "Playlist").
/// Returns Visible if the ActivePanel matches, Collapsed otherwise.
/// </summary>
public sealed class PanelToVisibilityConverter : IValueConverter
{
    public static readonly PanelToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SidebarPanel panel && parameter is string s &&
            Enum.TryParse<SidebarPanel>(s, true, out var target))
            return panel == target ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// 反向布尔转可见性：true→Collapsed, false→Visible。
/// 用于无视频时显示叠加层。
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public static readonly InverseBoolToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? (b ? Visibility.Collapsed : Visibility.Visible) : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// 布尔转可见性：true→Visible, false→Collapsed。
/// 用于活动条目内联编辑框等场景。
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
