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
