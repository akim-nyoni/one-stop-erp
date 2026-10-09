namespace One4All.Application.Municipal;

/// <summary>
/// Pro-rata settlement of a period's fixed charges when a property is transferred (brief §4.9): the seller pays for the days they owned the
/// property in the period and the buyer for the rest. What the seller was billed for the period so far (the whole period, or the instalments
/// to date) is compared with the seller's share; a surplus is credited to the seller and charged to the buyer, a shortfall the other way round
/// (later instalments then fall to the buyer as the new ratepayer). Pure.
/// </summary>
public static class TransferApportionment
{
    /// <summary>What the seller has been billed for the period by the transfer date.</summary>
    /// <param name="periodCharge">The period's charge.</param>
    /// <param name="slot">The period and instalment the transfer date falls in.</param>
    /// <returns>The amount billed so far.</returns>
    public static decimal BilledSoFar(decimal periodCharge, CycleSlot slot)
    {
        int billed = slot.Instalments == 1 ? 1 : slot.Instalment == 0 ? slot.Instalments : slot.Instalment;
        decimal total = 0m;
        for (int i = 1; i <= billed; i++)
        {
            total += BillingCycles.InstalmentAmount(periodCharge, slot with { Instalment = i });
        }

        return total;
    }

    /// <summary>The seller's share of the period: the days from the period start to the day before the transfer.</summary>
    /// <param name="periodCharge">The period's charge.</param>
    /// <param name="slot">The period.</param>
    /// <param name="transferDate">The day the buyer becomes owner.</param>
    /// <returns>The seller's share.</returns>
    public static decimal SellerShare(decimal periodCharge, CycleSlot slot, DateOnly transferDate)
    {
        int days = slot.PeriodEnd.DayNumber - slot.PeriodStart.DayNumber + 1;
        int owned = Math.Clamp(transferDate.DayNumber - slot.PeriodStart.DayNumber, 0, days);
        return decimal.Round(periodCharge * owned / days, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>The settlement: positive is credited to the seller and charged to the buyer; negative is charged to the seller and credited to the buyer.</summary>
    /// <param name="periodCharge">The period's charge.</param>
    /// <param name="slot">The period and instalment the transfer date falls in.</param>
    /// <param name="transferDate">The day the buyer becomes owner.</param>
    /// <returns>The settlement amount.</returns>
    public static decimal Settlement(decimal periodCharge, CycleSlot slot, DateOnly transferDate)
        => BilledSoFar(periodCharge, slot) - SellerShare(periodCharge, slot, transferDate);
}
