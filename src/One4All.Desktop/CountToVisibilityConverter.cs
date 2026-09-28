using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace One4All.Desktop;

/// <summary>Converts a count (int) to <see cref="Visibility.Visible"/> when positive, else collapsed.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
