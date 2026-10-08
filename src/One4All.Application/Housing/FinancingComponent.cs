namespace One4All.Application.Housing;

/// <summary>An instalment sale's discount for its financing component and how it unwinds.</summary>
/// <param name="PresentValue">The instalments' present value at the market rate on signature.</param>
/// <param name="Discount">How much the instalment receivable exceeds that present value (deferred, not sale revenue).</param>
/// <param name="Unwind">The discount earned with each instalment, by instalment number (summing to the discount).</param>
public sealed record FinancingDiscount(decimal PresentValue, decimal Discount, IReadOnlyDictionary<int, decimal> Unwind);

/// <summary>
/// The significant financing component of a long instalment sale (brief §4.10, IFRS 15.60–65): when a sale is paid over more than a year at a rate below
/// the market's, the revenue is the cash-selling price the instalments are worth today — their present value at the market rate — and the rest is
/// interest, earned over the term by the effective-interest method on top of any interest the plan itself charges.
/// </summary>
public static class FinancingComponent
{
    /// <summary>The discount a plan carries at the market rate, or null when it has none to account for.</summary>
    /// <param name="schedule">The planned instalments (amount and the plan's own interest on each).</param>
    /// <param name="principal">The amount financed (price less deposit).</param>
    /// <param name="annualMarketRatePercent">The market rate a buyer of this credit would pay.</param>
    /// <param name="signedOn">When control passed.</param>
    /// <param name="termMonths">The plan's term.</param>
    /// <returns>The discount and its unwind, or null for a term of a year or less, no market rate, or a plan already at or above it.</returns>
    public static FinancingDiscount? Measure(IReadOnlyList<PlannedInstalment> schedule, decimal principal, decimal? annualMarketRatePercent, DateOnly signedOn, int termMonths)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (annualMarketRatePercent is not decimal market || market <= 0m || termMonths <= 12 || schedule.Count == 0)
        {
            return null;
        }

        double monthly = (double)market / 1200d;
        decimal pv = decimal.Round(schedule.Sum(line => line.Amount / (decimal)Math.Pow(1d + monthly, Months(signedOn, line.DueDate))), 2, MidpointRounding.AwayFromZero);
        decimal discount = principal - pv;
        if (discount <= 0m)
        {
            return null;
        }

        // Effective interest at the market rate on the carrying amount, less the plan's own interest on the instalment, is what the discount earns.
        Dictionary<int, decimal> unwind = [];
        decimal carrying = pv, earned = 0m;
        DateOnly from = signedOn;
        List<PlannedInstalment> ordered = [.. schedule.OrderBy(l => l.Number)];
        for (int i = 0; i < ordered.Count; i++)
        {
            PlannedInstalment line = ordered[i];
            decimal interest = decimal.Round(carrying * (decimal)(Math.Pow(1d + monthly, Months(from, line.DueDate)) - 1d), 2, MidpointRounding.AwayFromZero);
            decimal portion = i == ordered.Count - 1 ? discount - earned : Math.Max(0m, Math.Min(discount - earned, interest - line.Interest));
            unwind[line.Number] = portion;
            earned += portion;
            carrying += interest - line.Amount;
            from = line.DueDate;
        }

        return new FinancingDiscount(pv, discount, unwind);
    }

    // Whole and part months between two dates.
    private static double Months(DateOnly from, DateOnly to)
    {
        int whole = ((to.Year - from.Year) * 12) + to.Month - from.Month;
        return Math.Max(0d, whole + ((to.Day - from.Day) / 30d));
    }
}
