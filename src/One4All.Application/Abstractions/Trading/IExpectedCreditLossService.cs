using One4All.Application.Trading;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>The expected credit loss assessment as at a date.</summary>
/// <param name="AsAtDate">The date.</param>
/// <param name="Bands">Exposure and provision per band.</param>
/// <param name="BookedAllowance">The allowance already on the allowance account at the date.</param>
public sealed record EclReport(DateOnly AsAtDate, IReadOnlyList<EclBandResult> Bands, decimal BookedAllowance)
{
    /// <summary>Total receivables assessed.</summary>
    public decimal TotalExposure => Bands.Sum(b => b.Exposure);

    /// <summary>The allowance the matrix calls for.</summary>
    public decimal TargetAllowance => Bands.Sum(b => b.Provision);

    /// <summary>What posting would charge (positive) or release (negative).</summary>
    public decimal Adjustment => TargetAllowance - BookedAllowance;
}

/// <summary>The result of booking the allowance.</summary>
/// <param name="Report">The assessment booked.</param>
/// <param name="JournalNumber">The adjusting journal, or null when the allowance already matched.</param>
public sealed record EclPostingResult(EclReport Report, string? JournalNumber);

/// <summary>
/// The IFRS 9 expected credit loss allowance on trade receivables (brief §4.5), simplified approach: receivables as at
/// the date - each customer's unallocated receipts and credit notes set against their oldest debts first - are banded by
/// days past due and provided at the matrix rates. The allowance sits on a contra-receivables account; posting adjusts
/// it to the target against bad-debt expense, so a repeat posts nothing and collections release it.
/// </summary>
public interface IExpectedCreditLossService
{
    /// <summary>Assesses the allowance without posting.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The date.</param>
    /// <param name="matrix">The provision matrix.</param>
    /// <param name="allowanceAccountCode">The contra-receivables allowance account (to read what is booked).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The report.</returns>
    Task<Result<EclReport>> AssessAsync(long companyId, DateOnly asAtDate, EclMatrix matrix, string allowanceAccountCode, CancellationToken ct = default);

    /// <summary>Assesses and books the adjustment.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAtDate">The date.</param>
    /// <param name="matrix">The provision matrix.</param>
    /// <param name="allowanceAccountCode">The allowance account (credited as it grows).</param>
    /// <param name="expenseAccountCode">The bad-debt expense account (debited as it grows).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The posting result.</returns>
    Task<Result<EclPostingResult>> PostAsync(long companyId, DateOnly asAtDate, EclMatrix matrix, string allowanceAccountCode, string expenseAccountCode, CancellationToken ct = default);
}
