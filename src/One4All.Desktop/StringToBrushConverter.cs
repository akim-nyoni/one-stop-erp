using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace One4All.Desktop;

/// <summary>Converts a hex colour string (e.g. <c>#1565C0</c>) to a <see cref="SolidColorBrush"/> for binding.</summary>
public sealed class StringToBrushConverter : IValueConverter
{
    private static readonly BrushConverter Inner = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return (SolidColorBrush)Inner.ConvertFromString(hex)!;
            }
            catch (FormatException)
            {
                // fall through to default
            }
        }

        return Brushes.Gray;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
