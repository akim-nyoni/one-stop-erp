using System.Globalization;

namespace One4All.Application.Localization;

/// <summary>
/// The desktop's text size steps (brief §12 accessibility: scalable fonts): 90% to 200%, larger and smaller a step at a time, with the
/// stored preference read back safely. Pure and unit-tested.
/// </summary>
public static class UiScale
{
    /// <summary>The sizes offered, smallest first.</summary>
    public static IReadOnlyList<double> Steps { get; } = [0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0];

    /// <summary>The next size up (the largest stays).</summary>
    /// <param name="current">The current scale.</param>
    /// <returns>The larger scale.</returns>
    public static double Larger(double current) => Steps.FirstOrDefault(s => s > current + 0.001, Steps[^1]);

    /// <summary>The next size down (the smallest stays).</summary>
    /// <param name="current">The current scale.</param>
    /// <returns>The smaller scale.</returns>
    public static double Smaller(double current) => Steps.LastOrDefault(s => s < current - 0.001, Steps[0]);

    /// <summary>Reads a stored scale, falling back to 100% for anything missing or out of range.</summary>
    /// <param name="stored">The stored text.</param>
    /// <returns>The scale.</returns>
    public static double Parse(string? stored)
        => double.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value >= Steps[0] && value <= Steps[^1] ? value : 1.0;

    /// <summary>The scale as a percentage label.</summary>
    /// <param name="scale">The scale.</param>
    /// <returns>E.g. "125%".</returns>
    public static string Label(double scale) => $"{Math.Round(scale * 100):0}%";
}
