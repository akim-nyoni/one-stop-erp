using One4All.Application.Municipal;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>An owner on the register.</summary>
/// <param name="CustomerCode">The owner's customer code.</param>
/// <param name="SharePercent">The share held.</param>
/// <param name="IsRatepayer">Whether this owner is billed for the property.</param>
/// <param name="OwnedFrom">The first day of ownership.</param>
/// <param name="OwnedTo">The last day, once transferred.</param>
/// <param name="Reference">The clearance certificate or register entry.</param>
public sealed record OwnerEntry(string CustomerCode, decimal SharePercent, bool IsRatepayer, DateOnly OwnedFrom, DateOnly? OwnedTo, string Reference);

/// <summary>
/// The owner register (brief §4.9): joint owners with shares, transfers that go through a valid rates clearance
/// certificate (each certificate used once) and move the ratepayer to the buyer, and the ownership history.
/// </summary>
public interface IOwnershipService
{
    /// <summary>Records a property's owners for the first time.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="owners">The owners and shares.</param>
    /// <param name="ownedFrom">Since when.</param>
    /// <param name="deedsReference">The title deed reference.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RegisterOwnersAsync(long companyId, long propertyId, IReadOnlyList<OwnerShare> owners, DateOnly ownedFrom, string? deedsReference, CancellationToken ct = default);

    /// <summary>
    /// Transfers a property: needs a clearance certificate for it, valid on the transfer date and not used before; ends
    /// the current owners' shares the day before, records the buyers and makes their ratepayer the property's. One unit.
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="buyers">The buyers and shares.</param>
    /// <param name="transferDate">The registration date of the transfer.</param>
    /// <param name="clearanceNumber">The rates clearance certificate.</param>
    /// <param name="deedsReference">The new title deed reference.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> TransferAsync(long companyId, long propertyId, IReadOnlyList<OwnerShare> buyers, DateOnly transferDate, string clearanceNumber, string? deedsReference, CancellationToken ct = default);

    /// <summary>The owners on a date.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="asAtDate">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The owners, the ratepayer first.</returns>
    Task<IReadOnlyList<OwnerEntry>> OwnersAsync(long companyId, long propertyId, DateOnly asAtDate, CancellationToken ct = default);

    /// <summary>Every owner the property has had, oldest first.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The history.</returns>
    Task<IReadOnlyList<OwnerEntry>> HistoryAsync(long companyId, long propertyId, CancellationToken ct = default);

    /// <summary>
    /// Settles the current period's fixed charges between the seller and buyer of a property transferred on a date (brief §4.9): for each
    /// unmetered service, what the seller was billed for the period is compared with the days they owned it; a surplus is credited to the
    /// seller and invoiced to the buyer (a shortfall the other way round). Metered services settle on the final reading. Run once, after the
    /// period has been billed.
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="transferDate">The transfer date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The settlement per tariff, or no transfer on the date, or already settled.</returns>
    Task<Result<IReadOnlyList<TransferSettlementLine>>> SettleTransferAsync(long companyId, long propertyId, DateOnly transferDate, CancellationToken ct = default);
}

/// <summary>A tariff's settlement on a transfer.</summary>
/// <param name="TariffCode">The tariff.</param>
/// <param name="PeriodStart">The first day of the period the transfer falls in.</param>
/// <param name="PeriodEnd">The last day of that period.</param>
/// <param name="PeriodCharge">The period's charge.</param>
/// <param name="BilledToSeller">What the seller was billed for the period so far.</param>
/// <param name="SellerShare">The seller's share for the days they owned the property.</param>
/// <param name="Settlement">Credited to the seller and charged to the buyer (negative: the other way round).</param>
public sealed record TransferSettlementLine(string TariffCode, DateOnly PeriodStart, DateOnly PeriodEnd, decimal PeriodCharge, decimal BilledToSeller, decimal SellerShare, decimal Settlement);
