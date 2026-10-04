using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A land acquisition to open.</summary>
public sealed class AcquisitionCase
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The parcel reference it will carry.</summary>
    public string ParcelNumber { get; init; } = string.Empty;

    /// <summary>The description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The area in square metres.</summary>
    public decimal AreaSquareMetres { get; init; }

    /// <summary>How it is acquired.</summary>
    public AcquisitionMethod Method { get; init; }

    /// <summary>The seller or grantor.</summary>
    public string Counterparty { get; init; } = string.Empty;

    /// <summary>The council resolution authorising it.</summary>
    public string ResolutionReference { get; init; } = string.Empty;

    /// <summary>When it is opened.</summary>
    public DateOnly Date { get; init; }
}

/// <summary>A cost to capitalise into an acquisition.</summary>
public sealed class AcquisitionCostInput
{
    /// <summary>The element of cost.</summary>
    public AcquisitionCostComponent Component { get; init; }

    /// <summary>The amount (base currency).</summary>
    public decimal Amount { get; init; }

    /// <summary>When it was incurred.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The supporting document.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>The land WIP account (debited).</summary>
    public string WipAccountCode { get; init; } = string.Empty;

    /// <summary>The bank or creditor account (credited).</summary>
    public string FundingAccountCode { get; init; } = string.Empty;
}

/// <summary>An acquisition's position.</summary>
/// <param name="AcquisitionNumber">The acquisition.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="BuildUp">The cost build-up by element.</param>
/// <param name="TotalCost">The total parcel cost.</param>
/// <param name="Outstanding">What stops it completing.</param>
public sealed record AcquisitionPosition(string AcquisitionNumber, AcquisitionStatus Status, IReadOnlyList<CostElement> BuildUp, decimal TotalCost, IReadOnlyList<string> Outstanding);

/// <summary>
/// Land acquisition with due diligence (brief §4.10.1): an acquisition is opened with its method, counterparty and
/// authorising resolution; its valuation is recorded; each due-diligence item is signed off by an officer; every cost is
/// capitalised into land WIP as incurred; and it completes — bringing the parcel into the land bank at its total built-up
/// cost — only once the checklist is fully and acceptably signed off. An abandoned acquisition's costs are written off.
/// </summary>
public interface ILandAcquisitionService
{
    /// <summary>Opens an acquisition.</summary>
    /// <param name="acquisition">The acquisition.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The acquisition number, or why it was refused.</returns>
    Task<Result<string>> OpenAsync(AcquisitionCase acquisition, CancellationToken ct = default);

    /// <summary>Records the valuation obtained.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="acquisitionNumber">The acquisition.</param>
    /// <param name="valuer">The valuer.</param>
    /// <param name="valuedOn">When.</param>
    /// <param name="value">The value.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RecordValuationAsync(long companyId, string acquisitionNumber, string valuer, DateOnly valuedOn, decimal value, CancellationToken ct = default);

    /// <summary>Signs off a due-diligence item (needs the acquisition Edit permission); a later sign-off replaces an earlier one.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="acquisitionNumber">The acquisition.</param>
    /// <param name="item">The item.</param>
    /// <param name="finding">What was found.</param>
    /// <param name="acceptable">Whether it is acceptable for the acquisition to proceed.</param>
    /// <param name="userId">The officer.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SignOffAsync(long companyId, string acquisitionNumber, DueDiligenceItem item, string finding, bool acceptable, long userId, DateOnly date, CancellationToken ct = default);

    /// <summary>Capitalises a cost: Dr land WIP, Cr the funding account.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="acquisitionNumber">The acquisition.</param>
    /// <param name="cost">The cost.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The journal number, or why it was refused.</returns>
    Task<Result<string>> AddCostAsync(long companyId, string acquisitionNumber, AcquisitionCostInput cost, CancellationToken ct = default);

    /// <summary>The cost build-up and what stops the acquisition completing.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="acquisitionNumber">The acquisition.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The position, or why there is none.</returns>
    Task<Result<AcquisitionPosition>> PositionAsync(long companyId, string acquisitionNumber, CancellationToken ct = default);

    /// <summary>Completes the acquisition: the parcel enters the land bank at its total cost.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="acquisitionNumber">The acquisition.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new parcel's id, or why it was refused.</returns>
    Task<Result<long>> CompleteAsync(long companyId, string acquisitionNumber, DateOnly date, CancellationToken ct = default);

    /// <summary>Abandons the acquisition, writing its capitalised costs off: Dr the expense account, Cr land WIP.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="acquisitionNumber">The acquisition.</param>
    /// <param name="date">When.</param>
    /// <param name="reason">Why.</param>
    /// <param name="expenseAccountCode">The expense account.</param>
    /// <param name="wipAccountCode">The land WIP account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The amount written off, or why it was refused.</returns>
    Task<Result<decimal>> AbandonAsync(long companyId, string acquisitionNumber, DateOnly date, string reason, string expenseAccountCode, string wipAccountCode, CancellationToken ct = default);
}
