using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace One4All.Desktop;

/// <summary>Visible when the value is set, collapsed when it is null.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is null ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
