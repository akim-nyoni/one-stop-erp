using System.Globalization;
using System.Windows.Data;

namespace One4All.Desktop;

/// <summary>The names of the till's categories by code, filled when the till loads, so the tabs read "Groceries" rather than "GROC".</summary>
public static class PosCategoryNames
{
    /// <summary>Gets the code → name map.</summary>
    public static System.Collections.Concurrent.ConcurrentDictionary<string, string> Map { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Shows a category code by its name when one is known.</summary>
public sealed class CategoryNameConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string code && PosCategoryNames.Map.TryGetValue(code, out string? name) ? name : value;

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
}
