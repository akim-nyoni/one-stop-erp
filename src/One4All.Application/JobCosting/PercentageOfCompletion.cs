namespace One4All.Application.JobCosting;

/// <summary>The outcome of a percentage-of-completion calculation.</summary>
/// <param name="PercentComplete">Cost-to-cost percentage complete, capped at 100.</param>
/// <param name="EarnedRevenue">Contract value × percentage complete.</param>
/// <param name="CertifiedToDate">Gross certified (billed) to date.</param>
/// <param name="CumulativeAdjustment">Earned − certified: positive is a contract asset (under-billed), negative a contract liability (over-billed).</param>
/// <param name="PriorAdjustment">The cumulative adjustment already on the books.</param>
public sealed record PocCalculation(decimal PercentComplete, decimal EarnedRevenue, decimal CertifiedToDate, decimal CumulativeAdjustment, decimal PriorAdjustment)
{
    /// <summary>The movement to post this run (cumulative − prior).</summary>
    public decimal Movement => CumulativeAdjustment - PriorAdjustment;

    /// <summary>True when the contract is under-billed (earned exceeds certified).</summary>
    public bool IsContractAsset => CumulativeAdjustment > 0m;

    /// <summary>True when the contract is over-billed (certified exceeds earned).</summary>
    public bool IsContractLiability => CumulativeAdjustment < 0m;
}

/// <summary>
/// Percentage-of-completion revenue recognition on the cost-to-cost input method (IFRS 15 over time):
/// revenue earned to date is the contract value in proportion to costs incurred against budget, and the gap
/// to what has been certified is carried as a contract asset or liability. Pure and unit-tested.
/// </summary>
public static class PercentageOfCompletion
{
    /// <summary>Computes the position for a contract.</summary>
    /// <param name="contractValue">The contract value.</param>
    /// <param name="budgetCost">The total budgeted cost (≤ 0 means no reliable estimate: 0% complete).</param>
    /// <param name="actualCost">Actual cost to date.</param>
    /// <param name="certifiedToDate">Gross certified to date.</param>
    /// <param name="priorAdjustment">The cumulative adjustment already posted.</param>
    /// <returns>The calculation.</returns>
    public static PocCalculation Compute(decimal contractValue, decimal budgetCost, decimal actualCost, decimal certifiedToDate, decimal priorAdjustment)
    {
        decimal percent = budgetCost <= 0m || actualCost <= 0m
            ? 0m
            : Math.Min(100m, decimal.Round(actualCost / budgetCost * 100m, 2, MidpointRounding.ToEven));
        decimal earned = decimal.Round(contractValue * percent / 100m, 2, MidpointRounding.ToEven);
        decimal cumulative = earned - certifiedToDate;
        return new PocCalculation(percent, earned, certifiedToDate, cumulative, priorAdjustment);
    }
}
