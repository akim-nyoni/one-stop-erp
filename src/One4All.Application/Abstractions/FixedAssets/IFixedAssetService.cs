using One4All.Core.Common;
using One4All.Core.FixedAssets;

namespace One4All.Application.Abstractions.FixedAssets;

/// <summary>Parameters for registering a fixed asset.</summary>
public sealed class RegisterAssetRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The asset number/tag.</summary>
    public string AssetNumber { get; init; } = string.Empty;

    /// <summary>The description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The asset class.</summary>
    public string AssetClass { get; init; } = string.Empty;

    /// <summary>The acquisition cost.</summary>
    public decimal Cost { get; init; }

    /// <summary>The residual value.</summary>
    public decimal ResidualValue { get; init; }

    /// <summary>The acquisition date.</summary>
    public DateOnly AcquisitionDate { get; init; }

    /// <summary>The depreciation method.</summary>
    public DepreciationMethod Method { get; init; }

    /// <summary>The useful life in months (straight line).</summary>
    public int UsefulLifeMonths { get; init; }

    /// <summary>The monthly reducing-balance rate (reducing balance).</summary>
    public decimal ReducingBalanceRatePercent { get; init; }

    /// <summary>The asset-cost account.</summary>
    public string CostAccountCode { get; init; } = string.Empty;

    /// <summary>The accumulated-depreciation account.</summary>
    public string AccumulatedDepreciationAccountCode { get; init; } = string.Empty;

    /// <summary>The depreciation-expense account.</summary>
    public string DepreciationExpenseAccountCode { get; init; } = string.Empty;

    /// <summary>The account funding the acquisition (credited), e.g. bank or a CWIP clearing account.</summary>
    public string FundingAccountCode { get; init; } = string.Empty;

    /// <summary>The acquisition document number.</summary>
    public string DocumentNumber { get; init; } = string.Empty;

    /// <summary>Gets the total units a units-of-production asset is expected to produce.</summary>
    public decimal? TotalEstimatedUnits { get; init; }

    /// <summary>Gets the asset this is a component of (by asset number), for an IAS 16 significant part.</summary>
    public string? ParentAssetNumber { get; init; }

    /// <summary>Gets the GL cost centre code the asset is analysed by.</summary>
    public string? CostCentre { get; init; }

    /// <summary>Gets the GL department code the asset is analysed by.</summary>
    public string? Department { get; init; }
}

/// <summary>The result of a depreciation run.</summary>
/// <param name="AssetsDepreciated">The number of assets depreciated.</param>
/// <param name="TotalDepreciation">The total charge posted.</param>
/// <param name="JournalNumber">The GL journal number, or null when nothing depreciated.</param>
/// <param name="SkippedNotYetAcquired">Active assets passed over because they were acquired after the run month.</param>
/// <param name="SkippedAlreadyDepreciated">Active assets passed over because the run month had already been charged.</param>
public readonly record struct DepreciationRunResult(int AssetsDepreciated, decimal TotalDepreciation, string? JournalNumber, int SkippedNotYetAcquired = 0, int SkippedAlreadyDepreciated = 0);

/// <summary>The result of disposing an asset.</summary>
/// <param name="Proceeds">The disposal proceeds.</param>
/// <param name="NetBookValue">The net book value at disposal.</param>
/// <param name="GainOrLoss">The profit (positive) or loss (negative) on disposal.</param>
/// <param name="JournalNumber">The disposal journal number.</param>
public readonly record struct DisposalResult(decimal Proceeds, decimal NetBookValue, decimal GainOrLoss, string JournalNumber);

/// <summary>The result of revaluing an asset.</summary>
/// <param name="Delta">The change to the carrying amount (+ uplift to reserve, − write-down to expense).</param>
/// <param name="NewCarryingAmount">The net book value after the revaluation.</param>
/// <param name="JournalNumber">The revaluation journal number.</param>
public readonly record struct RevaluationResult(decimal Delta, decimal NewCarryingAmount, string JournalNumber);

/// <summary>
/// Fixed Assets (brief §4.14). Registers assets, runs monthly depreciation posting to the GL, and disposes
/// assets recognising the profit or loss — all through the central posting engine.
/// </summary>
public interface IFixedAssetService
{
    /// <summary>Registers an asset and capitalises its cost: Dr asset cost, Cr the funding account.</summary>
    /// <param name="request">The registration request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created asset id.</returns>
    Task<Result<long>> RegisterAsync(RegisterAssetRequest request, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates an asset class: the method, life, residual percentage, rate and accounts an asset registered in it takes
    /// wherever its own request leaves them blank (brief §4.13).
    /// </summary>
    /// <param name="request">The class.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> SaveClassAsync(AssetClassRequest request, CancellationToken ct = default);

    /// <summary>
    /// Tests an asset for impairment (IAS 36): a recoverable amount below the carrying amount books the loss (Dr impairment, Cr accumulated
    /// depreciation and impairment); above it, an earlier loss is reversed — never beyond the loss held, nor above what the carrying amount
    /// would have been with no impairment. Straight-line depreciation then spreads the new carrying amount over the life left.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="assetId">The asset.</param>
    /// <param name="recoverableAmount">The recoverable amount.</param>
    /// <param name="date">The test date.</param>
    /// <param name="reason">The indicator (damage, idle, market fall...).</param>
    /// <param name="documentNumber">The journal document number.</param>
    /// <param name="impairmentAccountCode">The impairment account (blank: the asset class's).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loss (+) or reversal (−) booked and the new carrying amount.</returns>
    Task<Result<ImpairmentResult>> ImpairAsync(long companyId, long assetId, decimal recoverableAmount, DateOnly date, string reason, string documentNumber, string? impairmentAccountCode = null,
        CancellationToken ct = default);

    /// <summary>
    /// Runs one month's depreciation for every active asset, posting Dr depreciation expense, Cr
    /// accumulated depreciation for the period total. Full-month convention: an asset is charged from the
    /// month it was acquired in; assets acquired after the run month are skipped, and a month already charged
    /// for an asset is never charged again (so a re-run of the same month posts nothing).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="date">The run (posting) date.</param>
    /// <param name="documentNumber">The run document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run result.</returns>
    Task<Result<DepreciationRunResult>> RunDepreciationAsync(long companyId, DateOnly date, string documentNumber, CancellationToken ct = default);

    /// <summary>
    /// Disposes an asset for cash proceeds: clears cost and accumulated depreciation, credits the proceeds
    /// to the funding account, and recognises the profit or loss on disposal.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="assetId">The asset.</param>
    /// <param name="proceeds">The disposal proceeds.</param>
    /// <param name="date">The disposal date.</param>
    /// <param name="fundingAccountCode">The account receiving the proceeds (debited).</param>
    /// <param name="gainAccountCode">The profit-on-disposal account.</param>
    /// <param name="lossAccountCode">The loss-on-disposal account.</param>
    /// <param name="documentNumber">The disposal document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The disposal result.</returns>
    Task<Result<DisposalResult>> DisposeAsync(long companyId, long assetId, decimal proceeds, DateOnly date, string fundingAccountCode, string gainAccountCode, string lossAccountCode, string documentNumber, CancellationToken ct = default);

    /// <summary>
    /// Revalues an active asset to a new carrying amount (IAS 16): an uplift increases the asset cost against a
    /// revaluation-reserve equity account (Dr asset cost / Cr reserve); a write-down reduces the asset cost
    /// against an impairment-expense account (Dr expense / Cr asset cost). Future depreciation follows the
    /// revalued amount. Refuses a disposed asset or a negative carrying amount.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="assetId">The asset.</param>
    /// <param name="newCarryingAmount">The revalued net book value.</param>
    /// <param name="date">The revaluation date.</param>
    /// <param name="revaluationReserveAccountCode">The revaluation-reserve equity account (credited on an uplift).</param>
    /// <param name="impairmentExpenseAccountCode">The impairment-expense account (debited on a write-down).</param>
    /// <param name="documentNumber">The revaluation document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The revaluation result (nil-delta revaluations still succeed, posting nothing).</returns>
    Task<Result<RevaluationResult>> RevalueAsync(long companyId, long assetId, decimal newCarryingAmount, DateOnly date, string revaluationReserveAccountCode, string impairmentExpenseAccountCode, string documentNumber, CancellationToken ct = default);

    /// <summary>
    /// Transfers an asset to a new location and/or custodian (an asset-register movement — operational, no GL).
    /// A null location or custodian leaves that attribute unchanged. Refuses a disposed asset.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="assetId">The asset.</param>
    /// <param name="newLocation">The new location, or null to keep it.</param>
    /// <param name="newCustodian">The new custodian, or null to keep it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or an error.</returns>
    Task<Result> TransferAsync(long companyId, long assetId, string? newLocation, string? newCustodian, CancellationToken ct = default);

    /// <summary>
    /// Projects future depreciation across all active assets for a number of months from a start month, without
    /// posting anything. Each period carries the total charge for that month and the aggregate closing net book
    /// value, so the schedule can feed a budget or an asset-replacement plan.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="startMonth">The first month to forecast (day is ignored).</param>
    /// <param name="months">The number of months to project (positive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The depreciation forecast.</returns>
    Task<Result<DepreciationForecastReport>> DepreciationForecastAsync(long companyId, DateOnly startMonth, int months, CancellationToken ct = default);

    /// <summary>
    /// Moves an asset to another cost centre / department: a journal moves its cost and accumulated depreciation between the analyses,
    /// and its future depreciation follows. Refused for a disposed asset or an unknown code (<c>Fa.UnknownDimension</c>).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="assetId">The asset.</param>
    /// <param name="costCentre">The new cost centre code (blank for none).</param>
    /// <param name="department">The new department code (blank for none).</param>
    /// <param name="date">The transfer date.</param>
    /// <param name="documentNumber">The document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The journal number.</returns>
    Task<Result<string>> ReassignAsync(long companyId, long assetId, string? costCentre, string? department, DateOnly date, string documentNumber, CancellationToken ct = default);

    /// <summary>An asset's components (IAS 16 significant parts): asset number, cost and net book value.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="assetId">The parent asset.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The components.</returns>
    Task<IReadOnlyList<(string AssetNumber, string Description, decimal Cost, decimal NetBookValue)>> ComponentsAsync(long companyId, long assetId, CancellationToken ct = default);

    /// <summary>
    /// Records the units a units-of-production asset was used for (the run charges the month's readings); refused for another method
    /// (<c>Fa.NotUnitsOfProduction</c>), a month already depreciated (<c>Fa.PeriodDepreciated</c>) or non-positive units.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="assetId">The asset.</param>
    /// <param name="date">The date.</param>
    /// <param name="units">The units used.</param>
    /// <param name="reference">Where the reading came from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reading id.</returns>
    Task<Result<long>> RecordUsageAsync(long companyId, long assetId, DateOnly date, decimal units, string reference, CancellationToken ct = default);

    /// <summary>
    /// Logs a maintenance/service event against an asset (operational history — no GL posting; the spend is
    /// captured separately through the supplier invoice or expense claim that pays for it). Refuses an unknown
    /// or disposed asset, an empty description, or a negative cost.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="assetId">The asset serviced.</param>
    /// <param name="date">The service date.</param>
    /// <param name="description">What was done.</param>
    /// <param name="cost">The informational cost (non-negative).</param>
    /// <param name="provider">The service provider, if any.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created maintenance-log id.</returns>
    Task<Result<long>> RecordMaintenanceAsync(long companyId, long assetId, DateOnly date, string description, decimal cost, string? provider, CancellationToken ct = default);
}

/// <summary>One month of a depreciation forecast.</summary>
/// <param name="Period">The month label (yyyy-MM).</param>
/// <param name="Charge">The total depreciation charge across active assets that month.</param>
/// <param name="ClosingNetBookValue">The aggregate net book value at the end of the month.</param>
public sealed record DepreciationForecastPeriod(string Period, decimal Charge, decimal ClosingNetBookValue);

/// <summary>A forward depreciation schedule across all active assets.</summary>
/// <param name="StartMonth">The first month forecast.</param>
/// <param name="Months">The number of months projected.</param>
/// <param name="Periods">The per-month rows.</param>
public sealed record DepreciationForecastReport(DateOnly StartMonth, int Months, IReadOnlyList<DepreciationForecastPeriod> Periods);

/// <summary>An asset class to save.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name.</param>
/// <param name="Method">The default method.</param>
/// <param name="UsefulLifeMonths">The default life in months.</param>
/// <param name="ResidualPercent">The default residual as a % of cost.</param>
/// <param name="ReducingBalanceRatePercent">The default reducing-balance rate.</param>
/// <param name="CostAccountCode">The cost account.</param>
/// <param name="AccumulatedDepreciationAccountCode">The accumulated depreciation account.</param>
/// <param name="DepreciationExpenseAccountCode">The depreciation expense account.</param>
/// <param name="ImpairmentAccountCode">The impairment account.</param>
public sealed record AssetClassRequest(long CompanyId, string Code, string Name, DepreciationMethod Method, int UsefulLifeMonths, decimal ResidualPercent, decimal ReducingBalanceRatePercent,
    string CostAccountCode, string AccumulatedDepreciationAccountCode, string DepreciationExpenseAccountCode, string? ImpairmentAccountCode);

/// <summary>The outcome of an impairment test.</summary>
/// <param name="Amount">The loss (+) or reversal (−) booked (0 when none).</param>
/// <param name="CarryingAmount">The carrying amount after.</param>
/// <param name="JournalNumber">The journal (empty when nothing was booked).</param>
public sealed record ImpairmentResult(decimal Amount, decimal CarryingAmount, string JournalNumber);
