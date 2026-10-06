namespace One4All.Application.Procurement;

/// <summary>A scheduled delivery with what has been received against it.</summary>
/// <param name="DueDate">When it is due.</param>
/// <param name="Quantity">How much is due.</param>
/// <param name="Received">How much of it has arrived.</param>
public readonly record struct DeliverySlot(DateOnly DueDate, decimal Quantity, decimal Received)
{
    /// <summary>Gets what is still to come.</summary>
    public decimal Outstanding => Quantity - Received;
}

/// <summary>Matches an order line's receipts to its delivery schedule (brief §4.3).</summary>
public static class DeliverySchedule
{
    /// <summary>
    /// The line's deliveries with receipts matched earliest-due first. With no schedule the whole line is due on the fallback date; a
    /// schedule short of the ordered quantity (after an amendment up) puts the rest on its last date, and one beyond it (after an amendment
    /// down) is trimmed from the latest deliveries.
    /// </summary>
    /// <param name="ordered">The line's ordered quantity.</param>
    /// <param name="received">The line's received quantity.</param>
    /// <param name="schedule">The scheduled deliveries.</param>
    /// <param name="fallbackDue">When an unscheduled line is due.</param>
    /// <returns>The deliveries in due order.</returns>
    public static IReadOnlyList<DeliverySlot> Match(decimal ordered, decimal received, IEnumerable<(DateOnly Due, decimal Quantity)> schedule, DateOnly fallbackDue)
    {
        List<(DateOnly Due, decimal Quantity)> slots = [.. schedule.Where(s => s.Quantity > 0m).OrderBy(s => s.Due)];
        if (slots.Count == 0)
        {
            slots.Add((fallbackDue, ordered));
        }

        decimal excess = slots.Sum(s => s.Quantity) - ordered;
        if (excess < 0m)
        {
            slots[^1] = (slots[^1].Due, slots[^1].Quantity - excess);
        }

        for (int i = slots.Count - 1; i >= 0 && excess > 0m; i--)
        {
            decimal cut = Math.Min(excess, slots[i].Quantity);
            slots[i] = (slots[i].Due, slots[i].Quantity - cut);
            excess -= cut;
        }

        decimal left = received;
        List<DeliverySlot> matched = [];
        foreach ((DateOnly due, decimal quantity) in slots.Where(s => s.Quantity > 0m))
        {
            decimal take = Math.Max(0m, Math.Min(left, quantity));
            matched.Add(new DeliverySlot(due, quantity, take));
            left -= take;
        }

        return matched;
    }
}
