using One4All.Core.Municipal;

namespace One4All.Application.Municipal;

/// <summary>One month's slot in a charge's billing cycle.</summary>
/// <param name="PeriodStart">The first day of the period the month falls in.</param>
/// <param name="PeriodEnd">The last day of that period.</param>
/// <param name="Instalment">Which instalment this month bills (1-based), or 0 when it bills nothing (instalments are over).</param>
/// <param name="Instalments">How many instalments the period is billed in.</param>
public readonly record struct CycleSlot(DateOnly PeriodStart, DateOnly PeriodEnd, int Instalment, int Instalments)
{
    /// <summary>The month the bill number carries: the period start for a single bill, the month itself for an instalment.</summary>
    /// <param name="date">The bill date.</param>
    /// <returns>The first of that month.</returns>
    public DateOnly BillMonth(DateOnly date) => Instalments == 1 ? PeriodStart : new DateOnly(date.Year, date.Month, 1);
}

/// <summary>
/// Quarterly, half-yearly and annual billing cycles with optional monthly instalments (brief §4.9). A period's charge is billed once, by the
/// first run in the period (the bill number carries the period, so a second run finds it raised), or split into monthly instalments from
/// the period start, the last taking the rounding; a service in place for part of the period pays its share. Pure and unit-tested.
/// </summary>
public static class BillingCycles
{
    /// <summary>The slot a date falls in.</summary>
    /// <param name="cycle">The cycle.</param>
    /// <param name="startMonth">The month the cycle starts in.</param>
    /// <param name="instalments">Monthly instalments per period.</param>
    /// <param name="date">The bill date.</param>
    /// <returns>The slot.</returns>
    public static CycleSlot Slot(BillingCycle cycle, int startMonth, int instalments, DateOnly date)
    {
        int months = (int)cycle;
        int offset = ((date.Month - startMonth) % 12 + 12) % 12;
        int intoPeriod = offset % months;
        DateOnly start = new DateOnly(date.Year, date.Month, 1).AddMonths(-intoPeriod);
        DateOnly end = start.AddMonths(months).AddDays(-1);
        int n = Math.Clamp(instalments, 1, months);
        int instalment = n == 1 ? 1 : intoPeriod < n ? intoPeriod + 1 : 0;
        return new CycleSlot(start, end, instalment, n);
    }

    /// <summary>The share of a period a service was in place: days in place over days in the period.</summary>
    /// <param name="slot">The period.</param>
    /// <param name="start">The day the service started.</param>
    /// <param name="end">The last day of the service, if ended.</param>
    /// <returns>0–1.</returns>
    public static decimal Share(CycleSlot slot, DateOnly start, DateOnly? end)
    {
        DateOnly from = start > slot.PeriodStart ? start : slot.PeriodStart;
        DateOnly to = end is DateOnly e && e < slot.PeriodEnd ? e : slot.PeriodEnd;
        int days = to.DayNumber - from.DayNumber + 1;
        return days <= 0 ? 0m : (decimal)days / (slot.PeriodEnd.DayNumber - slot.PeriodStart.DayNumber + 1);
    }

    /// <summary>What one instalment bills of a period's charge (the last one takes the rounding).</summary>
    /// <param name="periodCharge">The period's charge.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>The amount billed this month.</returns>
    public static decimal InstalmentAmount(decimal periodCharge, CycleSlot slot)
    {
        if (slot.Instalment == 0)
        {
            return 0m;
        }

        if (slot.Instalments == 1)
        {
            return periodCharge;
        }

        decimal each = decimal.Round(periodCharge / slot.Instalments, 2, MidpointRounding.AwayFromZero);
        return slot.Instalment < slot.Instalments ? each : periodCharge - each * (slot.Instalments - 1);
    }
}
