using System.Globalization;
using System.Text;

namespace One4All.Application.Municipal;

/// <summary>A month's metered consumption.</summary>
/// <param name="Month">The first day of the month.</param>
/// <param name="Units">The units consumed (accepted readings in the month).</param>
public sealed record MonthlyConsumption(DateOnly Month, decimal Units);

/// <summary>A meter's consumption month by month.</summary>
/// <param name="Serial">The meter.</param>
/// <param name="Service">The service it measures.</param>
/// <param name="Months">Each month, oldest first (months with no reading show nothing).</param>
public sealed record MeterConsumption(string Serial, string Service, IReadOnlyList<MonthlyConsumption> Months)
{
    /// <summary>Gets the average monthly consumption over the months shown.</summary>
    public decimal Average => Months.Count == 0 ? 0m : decimal.Round(Months.Sum(m => m.Units) / Months.Count, 2);
}

/// <summary>
/// Draws the consumption graph printed on a ratepayer's bill or statement (brief §6 report 145): one bar per month, scaled to the busiest month,
/// with the units and the average, in plain text so it prints on any printer and in any email.
/// </summary>
public static class ConsumptionGraph
{
    /// <summary>Draws a meter's graph.</summary>
    /// <param name="meter">The meter's consumption.</param>
    /// <param name="width">The character width (at least 40).</param>
    /// <returns>The graph text.</returns>
    public static string Render(MeterConsumption meter, int width = 72)
    {
        ArgumentNullException.ThrowIfNull(meter);
        width = Math.Max(width, 40);
        StringBuilder sb = new();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Consumption - meter {meter.Serial} ({meter.Service})");
        decimal peak = meter.Months.Count == 0 ? 0m : meter.Months.Max(m => m.Units);
        int barWidth = width - 22;
        foreach (MonthlyConsumption month in meter.Months)
        {
            int length = peak <= 0m || month.Units <= 0m ? 0 : Math.Max(1, (int)Math.Round(month.Units / peak * barWidth, MidpointRounding.AwayFromZero));
            sb.AppendLine(CultureInfo.InvariantCulture, $"{month.Month:MMM yyyy} {new string('#', length).PadRight(barWidth)} {month.Units,10:0.##}");
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"{"Average",-8} {new string(' ', barWidth)} {meter.Average,10:0.##}");
        return sb.ToString();
    }
}
