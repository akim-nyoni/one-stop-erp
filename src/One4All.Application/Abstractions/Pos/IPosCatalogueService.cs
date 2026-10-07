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
public sealed record PosTile(long ItemId, string Code, string Name, string Category, decimal UnitPrice, string RevenueAccountCode, string? TaxCode, string? Barcode, string? Plu)
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
}
