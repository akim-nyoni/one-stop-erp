namespace One4All.Application.Housing;

/// <summary>A cancellation refund computation.</summary>
/// <param name="MonthsOccupied">Months occupied (part months count in full).</param>
/// <param name="OccupationRental">Rental for those months.</param>
/// <param name="AdminCharge">The admin charge on the cash price.</param>
/// <param name="Refund">Paid less rental, charge and arrears, never below zero.</param>
public sealed record RefundComputation(int MonthsOccupied, decimal OccupationRental, decimal AdminCharge, decimal Refund);

/// <summary>
/// The refund on cancelling an instalment agreement (brief §14 H53): what the buyer paid, less occupation rental for every
/// month or part month from signing to cancellation, less the admin charge (a percentage of the cash price), less the
/// instalments billed and unpaid - and nothing is refunded when those exceed what was paid. Pure and unit-tested.
/// </summary>
public static class CancellationRefund
{
    /// <summary>Computes the refund.</summary>
    /// <param name="paid">What the buyer paid, deposit included.</param>
    /// <param name="signedOn">When the agreement was signed.</param>
    /// <param name="cancelledOn">When it was cancelled.</param>
    /// <param name="monthlyRental">Occupation rental per month.</param>
    /// <param name="cashPrice">The stand's cash price.</param>
    /// <param name="adminChargePercent">The admin charge, in percent of the cash price.</param>
    /// <param name="arrears">Instalments billed and unpaid.</param>
    /// <returns>The computation, to the cent.</returns>
    public static RefundComputation Compute(decimal paid, DateOnly signedOn, DateOnly cancelledOn, decimal monthlyRental, decimal cashPrice, decimal adminChargePercent, decimal arrears)
    {
        int months = ((cancelledOn.Year - signedOn.Year) * 12) + cancelledOn.Month - signedOn.Month + (cancelledOn.Day > signedOn.Day ? 1 : 0);
        months = Math.Max(0, months);
        decimal rental = Math.Round(months * monthlyRental, 2, MidpointRounding.AwayFromZero);
        decimal admin = Math.Round(cashPrice * adminChargePercent / 100m, 2, MidpointRounding.AwayFromZero);
        return new RefundComputation(months, rental, admin, Math.Max(0m, paid - rental - admin - Math.Max(0m, arrears)));
    }
}
