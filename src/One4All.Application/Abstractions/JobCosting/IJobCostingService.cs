using One4All.Core.Common;
using One4All.Core.JobCosting;

namespace One4All.Application.Abstractions.JobCosting;

/// <summary>The result of certifying progress on a contract.</summary>
/// <param name="CertificateId">The created certificate id.</param>
/// <param name="Gross">The gross value certified this time.</param>
/// <param name="Retention">The retention held this time.</param>
/// <param name="NetPayable">The net raised on the client's AR account.</param>
/// <param name="OpenItemId">The AR open item for the net.</param>
/// <param name="ArJournalNumber">The AR journal number.</param>
/// <param name="RetentionJournalNumber">The journal recognising the retention receivable.</param>
public readonly record struct CertificateResult(long CertificateId, decimal Gross, decimal Retention, decimal NetPayable, long OpenItemId, string ArJournalNumber, string? RetentionJournalNumber);

/// <summary>A snapshot of job profitability (brief §4.12).</summary>
/// <param name="ContractValue">The contract value.</param>
/// <param name="BudgetCost">The budgeted cost.</param>
/// <param name="ActualCost">The actual cost captured to date.</param>
/// <param name="CertifiedToDate">The gross certified to date.</param>
/// <param name="PercentComplete">The cost-to-cost percentage complete.</param>
public readonly record struct JobProfitability(decimal ContractValue, decimal BudgetCost, decimal ActualCost, decimal CertifiedToDate, decimal PercentComplete)
{
    /// <summary>The forecast margin (contract value − budget cost).</summary>
    public decimal ForecastMargin => ContractValue - BudgetCost;
}

/// <summary>
/// Construction / Job Costing (brief §4.12). Manages contracts, captures actual costs for profitability,
/// and issues interim payment certificates whose net posts to AR with retention recognised separately.
/// </summary>
public interface IJobCostingService
{
    /// <summary>Creates a contract.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="contractNumber">The contract number.</param>
    /// <param name="name">The contract name.</param>
    /// <param name="customerId">The client account.</param>
    /// <param name="contractValue">The contract value.</param>
    /// <param name="budgetCost">The budgeted cost.</param>
    /// <param name="retentionPercent">The retention percentage.</param>
    /// <param name="revenueAccountCode">The contract-revenue account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created contract id.</returns>
    Task<Result<long>> CreateContractAsync(long companyId, string contractNumber, string name, long customerId, decimal contractValue, decimal budgetCost, decimal retentionPercent, string revenueAccountCode, CancellationToken ct = default);

    /// <summary>Captures an actual cost against a contract (recorded for profitability; the GL was hit at source).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="contractId">The contract.</param>
    /// <param name="costHead">The cost head / activity.</param>
    /// <param name="source">Where the cost came from.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="date">The date.</param>
    /// <param name="reference">A reference.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> CaptureCostAsync(long companyId, long contractId, string costHead, JobCostSource source, decimal amount, DateOnly date, string? reference, CancellationToken ct = default);

    /// <summary>
    /// Issues an interim payment certificate: raises the net payable on the client's AR account and
    /// recognises the retention as a retention receivable, both crediting contract revenue for the gross.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="contractId">The contract.</param>
    /// <param name="certificateNumber">The certificate number.</param>
    /// <param name="date">The valuation date.</param>
    /// <param name="workDoneToDate">The cumulative measured work to date.</param>
    /// <param name="materialsOnSite">Materials on site this valuation.</param>
    /// <param name="retentionReceivableAccountCode">The retention-receivable account (debited for retention).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The certificate result.</returns>
    Task<Result<CertificateResult>> CertifyProgressAsync(long companyId, long contractId, string certificateNumber, DateOnly date, decimal workDoneToDate, decimal materialsOnSite, string retentionReceivableAccountCode, CancellationToken ct = default);

    /// <summary>Returns a job profitability snapshot.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="contractId">The contract.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The profitability snapshot.</returns>
    Task<Result<JobProfitability>> GetProfitabilityAsync(long companyId, long contractId, CancellationToken ct = default);

    /// <summary>
    /// Releases held retention on a contract (e.g. at the end of the defects-liability period): moves the amount
    /// from the retention-receivable account onto the client's AR account so it ages and becomes collectible
    /// (Dr AR control / Cr retention receivable, creating an AR open item), and reduces the retention held.
    /// Refuses a non-positive amount or more than the retention held.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="contractId">The contract.</param>
    /// <param name="amount">The retention amount to release (positive, ≤ retention held).</param>
    /// <param name="date">The release date.</param>
    /// <param name="retentionReceivableAccountCode">The retention-receivable account the retention was held in (credited).</param>
    /// <param name="documentNumber">The release document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The release result.</returns>
    Task<Result<RetentionReleaseResult>> ReleaseRetentionAsync(long companyId, long contractId, decimal amount, DateOnly date, string retentionReceivableAccountCode, string documentNumber, CancellationToken ct = default);

    /// <summary>
    /// Period-end percentage-of-completion revenue recognition (cost-to-cost): brings cumulative revenue to
    /// contract value × % complete by posting the movement since the last run — an under-billed contract
    /// carries the excess of earned over certified as a contract asset (Dr asset / Cr revenue), an over-billed
    /// one carries the shortfall as a contract liability (Dr revenue / Cr liability). The prior position is
    /// unwound and the new one booked in one journal, so re-running after new costs or certificates only
    /// posts the difference; a run with no movement posts nothing.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="contractId">The contract.</param>
    /// <param name="date">The recognition date.</param>
    /// <param name="contractAssetAccountCode">The contract asset (unbilled revenue) account.</param>
    /// <param name="contractLiabilityAccountCode">The contract liability (over-billing) account.</param>
    /// <param name="documentNumber">The journal document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The recognition result.</returns>
    Task<Result<RevenueRecognitionResult>> RecogniseRevenueAsync(long companyId, long contractId, DateOnly date, string contractAssetAccountCode, string contractLiabilityAccountCode, string documentNumber, CancellationToken ct = default);
}

/// <summary>The result of a percentage-of-completion revenue recognition run.</summary>
/// <param name="PercentComplete">The cost-to-cost percentage complete.</param>
/// <param name="EarnedToDate">Revenue earned to date (contract value × % complete).</param>
/// <param name="CertifiedToDate">Gross certified to date.</param>
/// <param name="CumulativeAdjustment">Earned − certified now carried (positive asset, negative liability).</param>
/// <param name="Movement">The adjustment posted this run.</param>
/// <param name="JournalNumber">The journal posted, or null when there was no movement.</param>
public readonly record struct RevenueRecognitionResult(decimal PercentComplete, decimal EarnedToDate, decimal CertifiedToDate, decimal CumulativeAdjustment, decimal Movement, string? JournalNumber);

/// <summary>The result of releasing retention.</summary>
/// <param name="OpenItemId">The AR open item raised for the released retention.</param>
/// <param name="JournalNumber">The GL journal number.</param>
/// <param name="Amount">The amount released.</param>
public readonly record struct RetentionReleaseResult(long OpenItemId, string JournalNumber, decimal Amount);
