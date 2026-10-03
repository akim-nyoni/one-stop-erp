using One4All.Core.Municipal;

namespace One4All.Application.Municipal;

/// <summary>How far consumption may stray from what is expected before it is flagged.</summary>
/// <param name="HighFactor">Flag consumption above this multiple of the expected.</param>
/// <param name="LowFactor">Flag consumption below this multiple of the expected.</param>
public sealed record ReadingThresholds(decimal HighFactor = 2m, decimal LowFactor = 0.5m)
{
    /// <summary>The usual thresholds: double or half the expected consumption.</summary>
    public static ReadingThresholds Default { get; } = new();
}

/// <summary>What the meter knows before a reading.</summary>
/// <param name="LastActualReading">The last actual reading.</param>
/// <param name="EstimatedSinceActual">Consumption estimated since then.</param>
/// <param name="UnitsCredit">Over-estimated consumption still to net off.</param>
/// <param name="Digits">The number of dials (0 when unknown).</param>
/// <param name="Multiplier">The reading multiplier.</param>
public readonly record struct MeterState(decimal LastActualReading, decimal EstimatedSinceActual, decimal UnitsCredit, int Digits, decimal Multiplier);

/// <summary>The result of validating a reading.</summary>
/// <param name="RawConsumption">What the meter recorded since the last actual reading (× multiplier).</param>
/// <param name="BilledConsumption">What to bill, after estimates already billed and credits.</param>
/// <param name="UnitsCreditAfter">The over-estimate still to net off afterwards.</param>
/// <param name="Flags">What validation found.</param>
/// <param name="Accepted">Whether the reading is accepted (negative and tampered readings are not).</param>
public sealed record ReadingAssessment(decimal RawConsumption, decimal BilledConsumption, decimal UnitsCreditAfter, ReadingFlags Flags, bool Accepted);

/// <summary>A point in a meter's accepted actual reading history.</summary>
/// <param name="Date">The reading date.</param>
/// <param name="Consumption">The consumption recorded since the point before.</param>
public readonly record struct ConsumptionPoint(DateOnly Date, decimal Consumption);

/// <summary>
/// Meter reading validation and estimation (brief §4.9): rollover past the last dial, negative readings, high and low
/// consumption against the meter's own history, tamper flags, and estimates that the next actual reading trues up — an
/// over-estimate becomes a credit of units netted off later consumption. Pure and unit-tested.
/// </summary>
public static class MeterReadingRules
{
    /// <summary>Validates an actual reading.</summary>
    /// <param name="state">The meter before the reading.</param>
    /// <param name="reading">The reading.</param>
    /// <param name="expected">The consumption expected since the last actual reading (0 when there is no history).</param>
    /// <param name="thresholds">The high / low thresholds.</param>
    /// <param name="tamperSuspected">Whether the reader suspects tampering.</param>
    /// <returns>The assessment.</returns>
    public static ReadingAssessment Assess(MeterState state, decimal reading, decimal expected, ReadingThresholds thresholds, bool tamperSuspected)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        ReadingFlags flags = tamperSuspected ? ReadingFlags.Tamper : ReadingFlags.None;
        decimal advance = reading - state.LastActualReading;
        if (advance < 0m)
        {
            decimal capacity = state.Digits > 0 ? Pow10(state.Digits) : 0m;
            if (capacity > 0m && capacity - state.LastActualReading + reading < capacity / 2m)
            {
                advance = capacity - state.LastActualReading + reading;
                flags |= ReadingFlags.Rollover;
            }
            else
            {
                return new ReadingAssessment(0m, 0m, state.UnitsCredit, flags | ReadingFlags.Negative, false);
            }
        }

        decimal raw = advance * state.Multiplier;
        if (expected > 0m && raw > expected * thresholds.HighFactor)
        {
            flags |= ReadingFlags.HighConsumption;
        }

        if (expected > 0m && raw < expected * thresholds.LowFactor)
        {
            flags |= ReadingFlags.LowConsumption;
        }

        if (tamperSuspected)
        {
            return new ReadingAssessment(raw, 0m, state.UnitsCredit, flags, false);
        }

        if (state.EstimatedSinceActual > 0m && raw < state.EstimatedSinceActual)
        {
            flags |= ReadingFlags.EstimateCorrection;
        }

        decimal net = raw - state.EstimatedSinceActual - state.UnitsCredit;
        return new ReadingAssessment(raw, Math.Max(0m, net), Math.Max(0m, -net), flags, true);
    }

    /// <summary>The consumption expected over a number of days at the meter's historical daily rate, to 3 decimals.</summary>
    /// <param name="history">Accepted actual readings, oldest first; the first is the starting point.</param>
    /// <param name="days">The days to cover.</param>
    /// <returns>The expected consumption (0 without two readings spanning at least a day).</returns>
    public static decimal Expected(IReadOnlyList<ConsumptionPoint> history, int days)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.Count < 2 || days <= 0)
        {
            return 0m;
        }

        int span = history[^1].Date.DayNumber - history[0].Date.DayNumber;
        if (span <= 0)
        {
            return 0m;
        }

        decimal used = history.Skip(1).Sum(p => p.Consumption);
        return decimal.Round(used * days / span, 3, MidpointRounding.AwayFromZero);
    }

    /// <summary>The register value after an estimated consumption, wrapping past the last dial.</summary>
    /// <param name="lastReading">The last register value.</param>
    /// <param name="units">The consumption estimated.</param>
    /// <param name="digits">The number of dials (0 when unknown).</param>
    /// <param name="multiplier">The reading multiplier.</param>
    /// <returns>The estimated register value.</returns>
    public static decimal EstimatedReading(decimal lastReading, decimal units, int digits, decimal multiplier)
    {
        decimal reading = lastReading + (multiplier == 0m ? 0m : units / multiplier);
        decimal capacity = digits > 0 ? Pow10(digits) : 0m;
        return capacity > 0m && reading >= capacity ? reading - capacity : decimal.Round(reading, 4, MidpointRounding.AwayFromZero);
    }

    private static decimal Pow10(int digits)
    {
        decimal value = 1m;
        for (int i = 0; i < digits; i++)
        {
            value *= 10m;
        }

        return value;
    }
}
