using One4All.Core.Common;
using One4All.Core.Inventory;

namespace One4All.Application.Abstractions.Inventory;

/// <summary>An item found by a search.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Code">The code.</param>
/// <param name="Description">The description.</param>
/// <param name="Brand">The brand.</param>
/// <param name="MatchedOn">What matched (code, PLU, barcode, alias, brand, description).</param>
public sealed record ItemSearchHit(long ItemId, string Code, string Description, string? Brand, string MatchedOn);

/// <summary>What a scanned code or keyed PLU resolves to.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Code">The item code.</param>
/// <param name="UnitCode">The unit scanned (the base unit unless a pack barcode).</param>
/// <param name="Factor">Base units per unit scanned.</param>
public sealed record ItemLookup(long ItemId, string Code, string UnitCode, decimal Factor);

/// <summary>A supplier link to save.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="ItemId">The item.</param>
/// <param name="SupplierId">The supplier.</param>
/// <param name="SupplierItemCode">The supplier's code.</param>
/// <param name="LeadTimeDays">Lead time in days.</param>
/// <param name="MinimumOrderQuantity">Minimum order.</param>
/// <param name="LastCost">Last price paid.</param>
/// <param name="IsPreferred">Preferred source (the item's other links stop being preferred).</param>
public sealed record ItemSupplierRequest(long CompanyId, long ItemId, long SupplierId, string? SupplierItemCode, int LeadTimeDays, decimal MinimumOrderQuantity, decimal? LastCost, bool IsPreferred);

/// <summary>
/// The item master beyond the item itself (brief §4.2): extra barcodes and synonyms, pack units with their own barcodes and conversion
/// factors, supplier links (their codes, lead times, minimum orders, last costs, the preferred source), a live search over all of it, a
/// scan / PLU lookup that knows which unit was scanned, and ABC classification by sales value.
/// </summary>
public interface IItemCatalogueService
{
    /// <summary>Adds a barcode or synonym; a barcode already used by another item, pack or the item itself is refused (<c>Inv.BarcodeInUse</c>).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="kind">Barcode or synonym.</param>
    /// <param name="alias">The barcode or name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The alias id.</returns>
    Task<Result<long>> AddAliasAsync(long companyId, long itemId, ItemAliasKind kind, string alias, CancellationToken ct = default);

    /// <summary>Adds a pack unit (factor above zero; its barcode unique as above).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="unitCode">The unit.</param>
    /// <param name="factor">Base units per pack.</param>
    /// <param name="barcode">The pack's barcode.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The unit id.</returns>
    Task<Result<long>> AddUnitAsync(long companyId, long itemId, string unitCode, decimal factor, string? barcode, CancellationToken ct = default);

    /// <summary>Links (or updates the link to) a supplier.</summary>
    /// <param name="request">The link.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The link id.</returns>
    Task<Result<long>> LinkSupplierAsync(ItemSupplierRequest request, CancellationToken ct = default);

    /// <summary>Searches items by code, PLU, barcode (item, pack or alias), synonym, brand and description — exact matches first.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="term">What was typed.</param>
    /// <param name="take">How many to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The hits.</returns>
    Task<IReadOnlyList<ItemSearchHit>> SearchAsync(long companyId, string term, int take = 20, CancellationToken ct = default);

    /// <summary>Resolves a scanned barcode or keyed PLU to the item and the unit scanned (<c>Inv.NotFound</c> when nothing matches).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The barcode or PLU.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The lookup.</returns>
    Task<Result<ItemLookup>> LookupAsync(long companyId, string code, CancellationToken ct = default);

    /// <summary>
    /// Classifies stock items A / B / C by the value of their sales (stock issues) in a period: A the items making the first 80% of the
    /// value, B the next 15%, C the rest and anything unsold.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fromDate">The period start.</param>
    /// <param name="toDate">The period end.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many items fell in A, B and C.</returns>
    Task<(int A, int B, int C)> ClassifyAbcAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);

    /// <summary>Sets what the till checks when selling the item: the buyer's minimum age, the most per sale, and prescription-only.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="minimumAge">The minimum age (null: none).</param>
    /// <param name="maxQuantityPerSale">The most per sale (null: no limit).</param>
    /// <param name="requiresPrescription">Whether it is prescription-only.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SetSaleLimitsAsync(long companyId, long itemId, int? minimumAge, decimal? maxQuantityPerSale, bool requiresPrescription, CancellationToken ct = default);
}
