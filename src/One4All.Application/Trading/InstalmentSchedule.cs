namespace One4All.Application.Trading;

/// <summary>An instalment due under a payment arrangement.</summary>
/// <param name="Number">The instalment number, from 1.</param>
/// <param name="DueDate">When it is due.</param>
/// <param name="Amount">The amount due.</param>
public readonly record struct Instalment(int Number, DateOnly DueDate, decimal Amount);

/// <summary>Where a payment arrangement stands.</summary>
/// <param name="DueToDate">The instalments due (past the grace days) by the date.</param>
/// <param name="Paid">What has been paid since the arrangement started.</param>
/// <param name="Shortfall">What is due but unpaid.</param>
/// <param name="InstalmentsMissed">Instalments due but not covered by payments, taken in order.</param>
/// <param name="NextDue">The next instalment not yet covered, if any.</param>
/// <param name="Honoured">Whether everything has been paid.</param>
/// <param name="Defaulted">Whether enough instalments have been missed to default.</param>
public sealed record ArrangementStanding(decimal DueToDate, decimal Paid, decimal Shortfall, int InstalmentsMissed, Instalment? NextDue, bool Honoured, bool Defaulted);

/// <summary>
/// Payment arrangement schedules and default tracking (brief §4.9 debt management): monthly instalments from the start
/// date, the last one smaller where the total does not divide evenly, and an arrangement's standing on a date - paid in
/// order against the schedule, honoured once the total is paid, defaulted once a set number of instalments are missed.
/// Pure and unit-tested.
/// </summary>
public static class InstalmentSchedule
{
    /// <summary>Builds the monthly schedule.</summary>
    /// <param name="total">The total to pay.</param>
    /// <param name="instalment">The regular instalment.</param>
    /// <param name="start">The first instalment's due date.</param>
    /// <returns>The instalments.</returns>
    public static IReadOnlyList<Instalment> Build(decimal total, decimal instalment, DateOnly start)
    {
        if (total <= 0m || instalment <= 0m)
        {
            return [];
        }

        List<Instalment> schedule = [];
        decimal left = total;
        for (int n = 1; left > 0m; n++)
        {
            decimal amount = Math.Min(instalment, left);
            schedule.Add(new Instalment(n, start.AddMonths(n - 1), amount));
            left -= amount;
        }

        return schedule;
    }

    /// <summary>Works out an arrangement's standing.</summary>
    /// <param name="schedule">The schedule.</param>
    /// <param name="paid">What has been paid since the arrangement started.</param>
    /// <param name="asAtDate">The date.</param>
    /// <param name="graceDays">Days after the due date before an instalment counts as missed.</param>
    /// <param name="missedToDefault">Missed instalments that default the arrangement (at least 1).</param>
    /// <returns>The standing.</returns>
    public static ArrangementStanding Assess(IReadOnlyList<Instalment> schedule, decimal paid, DateOnly asAtDate, int graceDays, int missedToDefault)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        decimal total = schedule.Sum(i => i.Amount);
        decimal dueToDate = 0m;
        decimal covered = 0m;
        int missed = 0;
        Instalment? next = null;
        foreach (Instalment instalment in schedule)
        {
            covered += instalment.Amount;
            bool paidUp = paid >= covered;
            if (instalment.DueDate.AddDays(graceDays) < asAtDate)
            {
                dueToDate += instalment.Amount;
                if (!paidUp)
                {
                    missed++;
                }
            }

            if (!paidUp && next is null)
            {
                next = instalment;
            }
        }

        bool honoured = paid >= total;
        return new ArrangementStanding(dueToDate, paid, Math.Max(0m, dueToDate - paid), missed, next, honoured, !honoured && missed >= Math.Max(1, missedToDefault));
    }
}
