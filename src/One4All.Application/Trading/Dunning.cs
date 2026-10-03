namespace One4All.Application.Trading;

/// <summary>
/// The day thresholds at which a customer escalates through the dunning (reminder) stages, measured from
/// the oldest overdue item's due date.
/// </summary>
/// <param name="FirstReminderDays">Days overdue at which a first reminder (level 1) is due.</param>
/// <param name="SecondReminderDays">Days overdue at which a second reminder (level 2) is due.</param>
/// <param name="FinalNoticeDays">Days overdue at which a final notice (level 3) is due.</param>
public readonly record struct DunningStages(int FirstReminderDays, int SecondReminderDays, int FinalNoticeDays)
{
    /// <summary>Typical stages: 7 / 30 / 60 days overdue.</summary>
    public static DunningStages Default => new(7, 30, 60);
}

/// <summary>An overdue invoice, for working out what a customer still owes after their unallocated credits.</summary>
/// <param name="DueDate">When it fell due.</param>
/// <param name="Outstanding">Its outstanding amount.</param>
public readonly record struct OverdueItem(DateOnly DueDate, decimal Outstanding);

/// <summary>A customer's overdue position once their unallocated payments and credits are taken into account.</summary>
/// <param name="Amount">What is still overdue.</param>
/// <param name="OldestDaysOverdue">Days overdue of the oldest invoice not covered by credits (0 when all are covered).</param>
/// <param name="CreditsApplied">How much of the credits went against overdue invoices.</param>
public readonly record struct NetOverdue(decimal Amount, int OldestDaysOverdue, decimal CreditsApplied);

/// <summary>
/// The dunning-level rule: given how many days a customer's oldest item is overdue, returns the reminder
/// level (0 = up to date, 1 = first reminder, 2 = second reminder, 3 = final notice). Pure, so it is
/// unit-tested; the dunning service supplies the days from the AR open items.
/// </summary>
public static class Dunning
{
    /// <summary>Returns the dunning level for a number of days overdue.</summary>
    /// <param name="daysOverdue">Days the oldest overdue item is past its due date.</param>
    /// <param name="stages">The stage thresholds.</param>
    /// <returns>The dunning level (0–3).</returns>
    public static int LevelFor(int daysOverdue, DunningStages stages)
    {
        if (daysOverdue >= stages.FinalNoticeDays)
        {
            return 3;
        }

        if (daysOverdue >= stages.SecondReminderDays)
        {
            return 2;
        }

        if (daysOverdue >= stages.FirstReminderDays)
        {
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Sets a customer's unallocated payments and credit notes against their overdue invoices, oldest due first, as
    /// they will be when allocated. A customer whose credits cover the oldest invoices is graded by the oldest one
    /// still unpaid, and one whose credits cover everything is not chased at all.
    /// </summary>
    /// <param name="overdue">The overdue invoices.</param>
    /// <param name="unallocatedCredits">The customer's unallocated payments and credits.</param>
    /// <param name="asAtDate">The assessment date.</param>
    /// <returns>The net overdue position.</returns>
    public static NetOverdue ApplyCredits(IEnumerable<OverdueItem> overdue, decimal unallocatedCredits, DateOnly asAtDate)
    {
        ArgumentNullException.ThrowIfNull(overdue);

        decimal credit = Math.Max(0m, unallocatedCredits);
        decimal applied = 0m;
        decimal remaining = 0m;
        int oldest = 0;
        foreach (OverdueItem item in overdue.Where(i => i.Outstanding > 0m).OrderBy(i => i.DueDate))
        {
            decimal covered = Math.Min(credit, item.Outstanding);
            credit -= covered;
            applied += covered;

            decimal left = item.Outstanding - covered;
            if (left > 0m)
            {
                remaining += left;
                oldest = Math.Max(oldest, asAtDate.DayNumber - item.DueDate.DayNumber);
            }
        }

        return new NetOverdue(remaining, oldest, applied);
    }

    /// <summary>A human label for a dunning level.</summary>
    /// <param name="level">The dunning level (0–3).</param>
    /// <returns>The label.</returns>
    public static string LabelFor(int level) => level switch
    {
        1 => "First reminder",
        2 => "Second reminder",
        3 => "Final notice",
        _ => "Up to date",
    };
}
