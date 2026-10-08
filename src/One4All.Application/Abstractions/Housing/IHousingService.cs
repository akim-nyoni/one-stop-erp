using One4All.Core.Common;

namespace One4All.Application.Abstractions.Housing;

/// <summary>How a parcel's cost is apportioned across its stands (brief §4.10.2).</summary>
public enum ApportionMethod
{
    /// <summary>Equal share per stand.</summary>
    Equal = 0,

    /// <summary>Pro-rata by stand area.</summary>
    ByArea = 1,
}

/// <summary>A stand to create when subdividing a parcel.</summary>
/// <param name="StandNumber">The stand/erf number.</param>
/// <param name="SizeSquareMetres">The size in square metres.</param>
public readonly record struct NewStand(string StandNumber, decimal SizeSquareMetres);

/// <summary>The result of acquiring a parcel.</summary>
/// <param name="LandParcelId">The created parcel id.</param>
/// <param name="JournalNumber">The GL journal number for the WIP capitalisation.</param>
public readonly record struct AcquisitionResult(long LandParcelId, string JournalNumber);

/// <summary>The result of subdividing a parcel.</summary>
/// <param name="StandIds">The created stand ids.</param>
/// <param name="JournalNumber">The GL journal moving cost from WIP to Stands Available.</param>
public readonly record struct SubdivisionResult(IReadOnlyList<long> StandIds, string JournalNumber);

/// <summary>The result of selling a stand (transfer of control).</summary>
/// <param name="OpenItemId">The AR open item raised on the beneficiary.</param>
/// <param name="SaleJournalNumber">The AR sale journal number.</param>
/// <param name="CostOfSalesJournalNumber">The journal releasing the stand's cost to COGS.</param>
/// <param name="CostOfSales">The cost of sales released.</param>
public readonly record struct StandSaleResult(long OpenItemId, string SaleJournalNumber, string CostOfSalesJournalNumber, decimal CostOfSales);

/// <summary>
/// Housing, Land &amp; Estates (brief §4.10). Manages the land bank as IAS 2 inventory (WIP → Stands
/// Available → cost of sales on transfer of control) and the tamper-evident chain of ownership of every
/// stand from acquisition to title. Every money movement posts through the central engine.
/// </summary>
public interface IHousingService
{
    /// <summary>Creates a housing scheme (council-delivered, PPP, cooperative, employer-assisted); a code already in use is refused.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The scheme code.</param>
    /// <param name="name">The scheme name.</param>
    /// <param name="schemeType">The delivery model.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The scheme id.</returns>
    Task<Result<long>> CreateSchemeAsync(long companyId, string code, string name, Core.Housing.SchemeType schemeType, CancellationToken ct = default);

    /// <summary>Acquires a parcel into the land bank: Dr Land &amp; Housing Stock (WIP), Cr the funding account.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="parcelNumber">The parcel/title reference.</param>
    /// <param name="description">The description.</param>
    /// <param name="areaSquareMetres">The area.</param>
    /// <param name="cost">The acquisition cost.</param>
    /// <param name="date">The acquisition date.</param>
    /// <param name="wipAccountCode">The Land &amp; Housing Stock (WIP) account (debited).</param>
    /// <param name="fundingAccountCode">The bank/AP account (credited).</param>
    /// <param name="documentNumber">The document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The acquisition result.</returns>
    Task<Result<AcquisitionResult>> AcquireLandAsync(long companyId, string parcelNumber, string description, decimal areaSquareMetres, decimal cost, DateOnly date, string wipAccountCode, string fundingAccountCode, string documentNumber, CancellationToken ct = default);

    /// <summary>
    /// Subdivides a parcel into stands, apportioning its capitalised cost across them, and moves the cost
    /// from WIP to Stands Available for Sale: Dr Stands Available, Cr Land &amp; Housing Stock (WIP).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="landParcelId">The parcel to subdivide.</param>
    /// <param name="schemeId">The scheme the stands belong to, if any.</param>
    /// <param name="stands">The stands to create.</param>
    /// <param name="method">The cost-apportionment method.</param>
    /// <param name="date">The completion/servicing date.</param>
    /// <param name="wipAccountCode">The WIP account (credited).</param>
    /// <param name="standsAvailableAccountCode">The Stands Available for Sale account (debited).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The subdivision result.</returns>
    Task<Result<SubdivisionResult>> SubdivideAsync(long companyId, long landParcelId, long? schemeId, IReadOnlyList<NewStand> stands, ApportionMethod method, DateOnly date, string wipAccountCode, string standsAvailableAccountCode, CancellationToken ct = default);

    /// <summary>
    /// Sells a stand on transfer of control to a beneficiary: raises the price on the beneficiary's AR
    /// account and releases the stand's apportioned cost from Stands Available to cost of land sales.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="buyerCustomerId">The beneficiary's account.</param>
    /// <param name="buyerName">The beneficiary's name (for the ownership chain).</param>
    /// <param name="sellingPrice">The agreed price.</param>
    /// <param name="date">The transfer date.</param>
    /// <param name="salesAccountCode">The land-sales revenue account.</param>
    /// <param name="standsAvailableAccountCode">The Stands Available account (credited on cost release).</param>
    /// <param name="costOfSalesAccountCode">The cost-of-land-sales account (debited on cost release).</param>
    /// <param name="invoiceNumber">The sale invoice number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The sale result.</returns>
    Task<Result<StandSaleResult>> SellStandAsync(long companyId, long standId, long buyerCustomerId, string buyerName, decimal sellingPrice, DateOnly date, string salesAccountCode, string standsAvailableAccountCode, string costOfSalesAccountCode, string invoiceNumber, CancellationToken ct = default);

    /// <summary>
    /// Transfers ownership of a stand (cession, brief §4.10.6). Refuses unless the housing account is clear
    /// and dual approval has been recorded, then appends a link to the stand's tamper-evident chain.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="newHolderCustomerId">The new holder's account.</param>
    /// <param name="newHolderName">The new holder's name.</param>
    /// <param name="consideration">The consideration.</param>
    /// <param name="date">The cession date.</param>
    /// <param name="housingAccountClear">Whether the outgoing holder's housing account is clear.</param>
    /// <param name="dualApprovalRecorded">Whether the value-banded dual approval (§7) is in place.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or a rule error naming the failed gate.</returns>
    Task<Result> TransferOwnershipAsync(long companyId, long standId, long newHolderCustomerId, string newHolderName, decimal consideration, DateOnly date, bool housingAccountClear, bool dualApprovalRecorded, CancellationToken ct = default);
}
