using One4All.Application.Municipal;

namespace One4All.Application.Housing;

/// <summary>A deposit settled at the end of a tenancy.</summary>
/// <param name="AppliedToArrears">The part applied to what the tenant owes.</param>
/// <param name="Refund">The part refunded to the tenant.</param>
public sealed record DepositSettlement(decimal AppliedToArrears, decimal Refund);

/// <summary>
/// Council rental arithmetic (brief §4.10.7): the rent for a month a tenancy covers in part, how a deposit is split
/// between arrears and refund at the end, and the rent lost while a unit stands void. Pure and unit-tested.
/// </summary>
public static class RentalArithmetic
{
    /// <summary>The rent for a month, pro rata for the days the tenancy covers it.</summary>
    /// <param name="monthlyRent">The monthly rent.</param>
    /// <param name="month">Any day in the month.</param>
    /// <param name="start">The tenancy's first day.</param>
    /// <param name="end">The tenancy's last day, if it has ended.</param>
    /// <returns>The rent (0 when the tenancy does not cover the month).</returns>
    public static decimal RentFor(decimal monthlyRent, DateOnly month, DateOnly start, DateOnly? end)
        => ProRata.Apply(monthlyRent, ProRata.Fraction(month, start, end));

    /// <summary>A rent raised by an escalation percentage, to the cent.</summary>
    /// <param name="monthlyRent">The current rent.</param>
    /// <param name="percent">The escalation percentage.</param>
    /// <returns>The new rent.</returns>
    public static decimal Escalate(decimal monthlyRent, decimal percent) => decimal.Round(monthlyRent * (1m + percent / 100m), 2, MidpointRounding.AwayFromZero);

    /// <summary>Splits a deposit between what the tenant owes and a refund.</summary>
    /// <param name="deposit">The deposit held.</param>
    /// <param name="owed">What the tenant owes (nothing when in credit).</param>
    /// <returns>The split.</returns>
    public static DepositSettlement Settle(decimal deposit, decimal owed)
    {
        decimal applied = Math.Clamp(owed, 0m, deposit);
        return new DepositSettlement(applied, deposit - applied);
    }

    /// <summary>The days a unit has stood void, counting both ends.</summary>
    /// <param name="vacantSince">The first empty day.</param>
    /// <param name="asAtDate">The date.</param>
    /// <returns>The days (0 before it fell void).</returns>
    public static int DaysVoid(DateOnly vacantSince, DateOnly asAtDate) => Math.Max(0, asAtDate.DayNumber - vacantSince.DayNumber + 1);

    /// <summary>The rent lost over a number of void days, at the annual rent spread over 365 days.</summary>
    /// <param name="monthlyRent">The monthly rent.</param>
    /// <param name="days">The days void.</param>
    /// <returns>The rent lost, to the cent.</returns>
    public static decimal RentLost(decimal monthlyRent, int days) => decimal.Round(monthlyRent * 12m * days / 365m, 2, MidpointRounding.AwayFromZero);
}
