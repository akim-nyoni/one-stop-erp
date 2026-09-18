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
}

/// <summary>The result of a depreciation run.</summary>
/// <param name="AssetsDepreciated">The number of assets depreciated.</param>
/// <param name="TotalDepreciation">The total charge posted.</param>
/// <param name="JournalNumber">The GL journal number, or null when nothing depreciated.</param>
public readonly record struct DepreciationRunResult(int AssetsDepreciated, decimal TotalDepreciation, string? JournalNumber);

/// <summary>The result of disposing an asset.</summary>
/// <param name="Proceeds">The disposal proceeds.</param>
/// <param name="NetBookValue">The net book value at disposal.</param>
/// <param name="GainOrLoss">The profit (positive) or loss (negative) on disposal.</param>
/// <param name="JournalNumber">The disposal journal number.</param>
public readonly record struct DisposalResult(decimal Proceeds, decimal NetBookValue, decimal GainOrLoss, string JournalNumber);

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
    /// Runs one month's depreciation for every active asset, posting Dr depreciation expense, Cr
    /// accumulated depreciation for the period total.
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
}
