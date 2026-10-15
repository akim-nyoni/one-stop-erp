namespace One4All.Application.Inventory;

/// <summary>How often each class of item is counted, and how many items one cycle count takes.</summary>
/// <param name="ADays">Days between counts of an A item (the fast, valuable ones).</param>
/// <param name="BDays">Days between counts of a B item.</param>
/// <param name="CDays">Days between counts of a C item (and of an unclassified one).</param>
/// <param name="MaxItems">The most items one count takes; the most overdue go first.</param>
public sealed record CycleCountRule(int ADays = 30, int BDays = 90, int CDays = 180, int MaxItems = 50)
{
    /// <summary>The days between counts for an item's ABC class.</summary>
    /// <param name="abcClass">The class (A, B, C or none).</param>
    /// <returns>The interval in days.</returns>
    public int IntervalFor(string? abcClass) => abcClass?.Trim().ToUpperInvariant() switch
    {
        "A" => ADays,
        "B" => BDays,
        _ => CDays,
    };
}

/// <summary>An item in the warehouse, with its class and when it was last counted there.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="AbcClass">Its ABC class, if classified.</param>
/// <param name="LastCounted">The date of its last posted count in the warehouse; null if never counted.</param>
public readonly record struct CycleCountCandidate(long ItemId, string? AbcClass, DateOnly? LastCounted);

/// <summary>An item due a count, and by how many days it is overdue (never-counted items are the most overdue).</summary>
/// <param name="ItemId">The item.</param>
/// <param name="AbcClass">Its class.</param>
/// <param name="DaysOverdue">Days past its due date (int.MaxValue when never counted).</param>
public readonly record struct CycleCountDue(long ItemId, string? AbcClass, int DaysOverdue);

/// <summary>
/// Picks the items a cycle count takes (brief §4.2): each item is due once its class's interval has passed since its last count; never-counted
/// items first, then A before B before C, then the most overdue; at most the rule's limit. Over a year every item is counted at its class's rate
/// without closing the warehouse for a full stocktake.
/// </summary>
public static class CycleCountPlan
{
    /// <summary>Chooses the items due a count on a date.</summary>
    /// <param name="candidates">The warehouse's items.</param>
    /// <param name="rule">The intervals and the limit.</param>
    /// <param name="date">The count date.</param>
    /// <returns>The items to count, most urgent first.</returns>
    public static IReadOnlyList<CycleCountDue> Due(IEnumerable<CycleCountCandidate> candidates, CycleCountRule rule, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(rule);
        List<CycleCountDue> due = [];
        foreach (CycleCountCandidate candidate in candidates)
        {
            if (candidate.LastCounted is not DateOnly last)
            {
                due.Add(new CycleCountDue(candidate.ItemId, candidate.AbcClass, int.MaxValue));
                continue;
            }

            int overdue = date.DayNumber - last.DayNumber - rule.IntervalFor(candidate.AbcClass);
            if (overdue >= 0)
            {
                due.Add(new CycleCountDue(candidate.ItemId, candidate.AbcClass, overdue));
            }
        }

        return [.. due.OrderByDescending(d => d.DaysOverdue == int.MaxValue).ThenBy(d => Rank(d.AbcClass)).ThenByDescending(d => d.DaysOverdue).ThenBy(d => d.ItemId)
            .Take(Math.Max(1, rule.MaxItems))];
    }

    private static int Rank(string? abcClass) => abcClass?.Trim().ToUpperInvariant() switch
    {
        "A" => 0,
        "B" => 1,
        _ => 2,
    };
}
