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
