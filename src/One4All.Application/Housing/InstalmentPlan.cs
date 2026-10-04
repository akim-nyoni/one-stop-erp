using One4All.Core.Housing;

namespace One4All.Application.Housing;

/// <summary>One instalment of a plan.</summary>
/// <param name="Number">The instalment number, from 1.</param>
/// <param name="DueDate">When it falls due.</param>
/// <param name="Amount">The instalment.</param>
/// <param name="Interest">The interest part.</param>
/// <param name="Principal">The principal part.</param>
/// <param name="BalanceAfter">The principal still owed after it.</param>
public sealed record PlannedInstalment(int Number, DateOnly DueDate, decimal Amount, decimal Interest, decimal Principal, decimal BalanceAfter);

/// <summary>An early-settlement quote.</summary>
/// <param name="PrincipalOutstanding">The principal still owed after the instalments due by the date.</param>
/// <param name="InterestRebated">The interest on later instalments that is no longer charged.</param>
public sealed record SettlementQuote(decimal PrincipalOutstanding, decimal InterestRebated);

/// <summary>
/// Instalment-sale arithmetic (brief §4.10.5): the monthly schedule for zero, flat or reducing-balance interest, every
/// figure to the cent with the last instalment absorbing rounding so the principal clears exactly; and the
/// early-settlement quote, which rebates the interest of instalments not yet due. Pure and unit-tested.
/// </summary>
public static class InstalmentPlan
{
    /// <summary>Builds the schedule.</summary>
    /// <param name="principal">The amount financed (price less deposit).</param>
    /// <param name="annualRatePercent">The annual interest rate.</param>
    /// <param name="months">The term in months.</param>
    /// <param name="method">How interest is charged.</param>
    /// <param name="firstDueDate">The first instalment's due date; later ones fall monthly after it.</param>
    /// <returns>The instalments.</returns>
    public static IReadOnlyList<PlannedInstalment> Build(decimal principal, decimal annualRatePercent, int months, InterestMethod method, DateOnly firstDueDate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(months, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(principal);
        ArgumentOutOfRangeException.ThrowIfNegative(annualRatePercent);
        decimal rate = method == InterestMethod.Zero ? 0m : annualRatePercent;
        return method == InterestMethod.ReducingBalance && rate > 0m
            ? Reducing(principal, rate / 1200m, months, firstDueDate)
            : Even(principal, method == InterestMethod.Flat ? Round(principal * rate / 100m * months / 12m) : 0m, months, firstDueDate);
    }

    /// <summary>The early-settlement quote as at a date: instalments due by then stand; later interest is rebated.</summary>
    /// <param name="schedule">The schedule.</param>
    /// <param name="asAtDate">The settlement date.</param>
    /// <returns>The quote.</returns>
    public static SettlementQuote Settle(IReadOnlyList<PlannedInstalment> schedule, DateOnly asAtDate)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        PlannedInstalment? lastDue = schedule.LastOrDefault(i => i.DueDate <= asAtDate);
        decimal principal = lastDue?.BalanceAfter ?? schedule.Sum(i => i.Principal);
        decimal rebate = schedule.Where(i => i.DueDate > asAtDate).Sum(i => i.Interest);
        return new SettlementQuote(principal, rebate);
    }

    // Principal and interest spread evenly (zero or flat), the last instalment taking the rounding.
    private static List<PlannedInstalment> Even(decimal principal, decimal totalInterest, int months, DateOnly first)
    {
        decimal principalPart = Round(principal / months);
        decimal interestPart = Round(totalInterest / months);
        List<PlannedInstalment> lines = [];
        decimal balance = principal;
        decimal interestLeft = totalInterest;
        for (int n = 1; n <= months; n++)
        {
            bool last = n == months;
            decimal p = last ? balance : principalPart;
            decimal i = last ? interestLeft : interestPart;
            balance -= p;
            interestLeft -= i;
            lines.Add(new PlannedInstalment(n, first.AddMonths(n - 1), p + i, i, p, balance));
        }

        return lines;
    }

    // An amortising annuity: a level payment, interest on the balance still owed, the last payment clearing it.
    private static List<PlannedInstalment> Reducing(decimal principal, decimal monthlyRate, int months, DateOnly first)
    {
        double r = (double)monthlyRate;
        decimal payment = Round(principal * (decimal)(r / (1 - Math.Pow(1 + r, -months))));
        List<PlannedInstalment> lines = [];
        decimal balance = principal;
        for (int n = 1; n <= months; n++)
        {
            decimal interest = Round(balance * monthlyRate);
            decimal p = n == months ? balance : Math.Min(balance, payment - interest);
            balance -= p;
            lines.Add(new PlannedInstalment(n, first.AddMonths(n - 1), p + interest, interest, p, balance));
        }

        return lines;
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
