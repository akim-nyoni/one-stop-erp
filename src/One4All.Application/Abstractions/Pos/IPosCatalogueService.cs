using One4All.Core.Common;

namespace One4All.Application.Abstractions.Pos;

/// <summary>One product tile on the till: a stock-master item with its price for one unit.</summary>
/// <param name="ItemId">The item sold (stock and cost of sales follow it).</param>
/// <param name="Code">Its code.</param>
/// <param name="Name">What the tile shows.</param>
/// <param name="Category">The tile group.</param>
/// <param name="UnitPrice">The price of one, before tax.</param>
/// <param name="RevenueAccountCode">Where the sale posts.</param>
/// <param name="TaxCode">Its tax code, if taxed.</param>
/// <param name="Barcode">Its barcode, for searching.</param>
/// <param name="Plu">Its PLU, for searching.</param>
public sealed record PosTile(long ItemId, string Code, string Name, string Category, decimal UnitPrice, string RevenueAccountCode, string? TaxCode, string? Barcode, string? Plu,
    long? DepositItemId = null)
{
    /// <summary>Whether the tile matches what the cashier has typed: every word found in its code, name, barcode or PLU (any case).</summary>
    /// <param name="text">The search text.</param>
    /// <returns>True when it matches (or nothing is typed).</returns>
    public bool Matches(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        string haystack = string.Join(' ', Code, Name, Barcode, Plu);
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => haystack.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>A category at the till.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name.</param>
/// <param name="HasChildren">Whether there are categories under it.</param>
public sealed record PosCategory(string Code, string Name, bool HasChildren);

/// <summary>The till's product tiles (brief §4.1), drawn from the item master so every tile sale moves stock and posts its cost.</summary>
public interface IPosCatalogueService
{
    /// <summary>
    /// The sellable items as tiles: active, with a revenue account and a price for one from the default price list (an explicit price,
    /// or the list's cost-plus rule); items with no price are left off (they can still be scanned once priced).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The tiles by category and name.</returns>
    Task<IReadOnlyList<PosTile>> TilesAsync(long companyId, CancellationToken ct = default);

    /// <summary>Searches the whole item master on the server - code, PLU, barcodes (item, pack or alias), synonyms, brand and description - and prices the hits.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="term">What was typed or scanned.</param>
    /// <param name="take">How many to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tiles, best match first.</returns>
    Task<IReadOnlyList<PosTile>> SearchAsync(long companyId, string term, int take = 30, CancellationToken ct = default);

    /// <summary>
    /// The till's product finder (brief §4.4 "filters: department, category, brand, supplier, price band, in-stock only, promo only, recently sold"):
    /// the sellable items that pass every filter given, priced, by name.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="filter">The filters.</param>
    /// <param name="asAt">Today (promotions in force, recent sales).</param>
    /// <param name="take">How many to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tiles.</returns>
    Task<IReadOnlyList<PosTile>> FindAsync(long companyId, PosFinderFilter filter, DateOnly asAt, int take = 60, CancellationToken ct = default);

    /// <summary>The brands and supplier codes the finder can filter on.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The brands and supplier codes, sorted.</returns>
    Task<(IReadOnlyList<string> Brands, IReadOnlyList<string> Suppliers)> FinderChoicesAsync(long companyId, CancellationToken ct = default);

    /// <summary>The till's favourites: its own first, then those for every till.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="terminalId">The terminal.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tiles in their order.</returns>
    Task<IReadOnlyList<PosTile>> FavouritesAsync(long companyId, long? terminalId, CancellationToken ct = default);

    /// <summary>Pins, re-orders or removes a favourite.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="terminalId">The terminal (null: every till).</param>
    /// <param name="itemCode">The item.</param>
    /// <param name="sortOrder">Where it sits.</param>
    /// <param name="remove">True to remove it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> SetFavouriteAsync(long companyId, long? terminalId, string itemCode, int sortOrder, bool remove, CancellationToken ct = default);

    /// <summary>
    /// Pins (or unpins) a favourite for a cashier and / or a till, on a page, in a colour (brief §4.1 "favourites / hot-key grid per terminal and per cashier:
    /// colour-coded … pages of tiles").
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="terminalId">The till (null: every till).</param>
    /// <param name="userId">The cashier (null: whoever uses the till).</param>
    /// <param name="itemCode">The item.</param>
    /// <param name="sortOrder">Its position on the page.</param>
    /// <param name="page">The page (1 first).</param>
    /// <param name="colour">The tile colour, or null.</param>
    /// <param name="remove">Unpin it instead.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetFavouriteAsync(long companyId, long? terminalId, long? userId, string itemCode, int sortOrder, int page, string? colour, bool remove, CancellationToken ct = default);

    /// <summary>
    /// Saves a cashier's own arrangement of a favourites page at a till (dragged into order): the cashier gets their own pin for each item, in that
    /// order, keeping the colour the item showed in.
    /// </summary>
    Task<Result> ArrangeFavouritesAsync(long companyId, long? terminalId, long userId, int page, IReadOnlyList<long> itemIds, CancellationToken ct = default);

    /// <summary>The favourites a cashier sees at a till: their own first, then the till's, then every till's - each item once - with colour and page.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="terminalId">The till.</param>
    /// <param name="userId">The cashier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The favourite tiles in page and position order.</returns>
    Task<IReadOnlyList<FavouriteTile>> FavouriteTilesAsync(long companyId, long? terminalId, long? userId, CancellationToken ct = default);

    /// <summary>The categories under a category (null: the top level), for drilling down.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="parentCode">The parent category.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The categories.</returns>
    Task<IReadOnlyList<PosCategory>> CategoriesAsync(long companyId, string? parentCode, CancellationToken ct = default);

    /// <summary>The tiles in a category and every category under it.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="categoryCode">The category.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tiles.</returns>
    Task<IReadOnlyList<PosTile>> CategoryTilesAsync(long companyId, string categoryCode, CancellationToken ct = default);
}

/// <summary>The till's product finder filters; each one left empty does not filter.</summary>
/// <param name="Category">The category (its sub-categories included), or null.</param>
/// <param name="Brand">The brand, or null.</param>
/// <param name="SupplierCode">Items this supplier supplies, or null.</param>
/// <param name="MinPrice">The lowest price, or null.</param>
/// <param name="MaxPrice">The highest price, or null.</param>
/// <param name="InStockOnly">Only items with stock on hand.</param>
/// <param name="PromotionOnly">Only items on a promotion in force.</param>
/// <param name="SoldWithinDays">Only items sold at the tills in the last so many days, or null.</param>
public sealed record PosFinderFilter(string? Category = null, string? Brand = null, string? SupplierCode = null, decimal? MinPrice = null, decimal? MaxPrice = null,
    bool InStockOnly = false, bool PromotionOnly = false, int? SoldWithinDays = null);

/// <summary>A favourite on the till: the tile, its colour and page.</summary>
/// <param name="Tile">The priced tile.</param>
/// <param name="Colour">The tile colour, or null.</param>
/// <param name="Page">The page (1 first).</param>
public sealed record FavouriteTile(PosTile Tile, string? Colour, int Page);
