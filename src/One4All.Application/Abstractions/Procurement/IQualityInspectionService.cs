using One4All.Core.Common;

namespace One4All.Application.Abstractions.Procurement;

/// <summary>One item's inspection result.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Passed">The quantity passed, to release to stock.</param>
/// <param name="Rejected">The quantity rejected, to return to the supplier.</param>
/// <param name="RejectionReason">Why, when anything is rejected.</param>
public sealed record InspectionResult(long ItemId, decimal Passed, decimal Rejected, string? RejectionReason = null);

/// <summary>An inspection of goods held in quarantine.</summary>
/// <param name="InspectedOn">When.</param>
/// <param name="InspectorUserId">Who inspected.</param>
/// <param name="ReleaseWarehouseId">Where passed goods go.</param>
/// <param name="PriceVarianceAccountCode">Where any difference on returning rejected goods goes.</param>
/// <param name="Results">The results per item.</param>
public sealed record InspectionRequest(DateOnly InspectedOn, long InspectorUserId, long ReleaseWarehouseId, string PriceVarianceAccountCode, IReadOnlyList<InspectionResult> Results);

/// <summary>What an inspection did.</summary>
/// <param name="InspectionId">The inspection record.</param>
/// <param name="Released">Units released to stock.</param>
/// <param name="Returned">Units returned to the supplier.</param>
/// <param name="CreditNoteValue">The supplier credit for the returned units.</param>
public sealed record InspectionOutcome(long InspectionId, decimal Released, decimal Returned, decimal CreditNoteValue);

/// <summary>
/// Quality inspection holds on goods received (brief §4.3): goods received into a quarantine warehouse are held out of
/// available stock until inspected; what passes is released to stock and what fails is returned to the supplier for
/// credit, in one unit, with the result kept.
/// </summary>
public interface IQualityInspectionService
{
    /// <summary>Inspects goods held from a receipt; no more than is still uninspected for each item.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="goodsReceivedNoteId">The receipt.</param>
    /// <param name="request">The results.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was released and returned, or why it was refused.</returns>
    Task<Result<InspectionOutcome>> InspectAsync(long companyId, long goodsReceivedNoteId, InspectionRequest request, CancellationToken ct = default);
}
