using One4All.Core.Common;

namespace One4All.Core.Restaurant;

/// <summary>
/// A menu item (brief: Restaurant module, <c>res.menu_item</c>) — a sellable composite that is <b>not</b>
/// stocked itself. Its price is what the guest pays; its cost is derived by exploding its recipe into the
/// ingredient stock items, which <i>are</i> stock-tracked. This keeps menu items off the stock ledger while
/// their ingredients remain valued at moving-average cost.
/// </summary>
public sealed class MenuItem : SoftDeletableEntity, ICompanyScoped
{
    private readonly List<RecipeLine> _recipe = [];
    private readonly List<MenuItemPrice> _prices = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private MenuItem()
    {
        Code = string.Empty;
        Name = string.Empty;
        RevenueAccountCode = string.Empty;
        CostOfSalesAccountCode = string.Empty;
    }

    /// <summary>Creates a menu item.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The menu code.</param>
    /// <param name="name">The menu name.</param>
    /// <param name="price">The selling price (tax-exclusive net).</param>
    /// <param name="revenueAccountCode">The revenue account credited.</param>
    /// <param name="costOfSalesAccountCode">The cost-of-sales account debited on the recipe explosion.</param>
    /// <param name="taxCode">The tax code, if any.</param>
    public MenuItem(long companyId, string code, string name, decimal price, string revenueAccountCode, string costOfSalesAccountCode, string? taxCode = null)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        Price = price;
        RevenueAccountCode = revenueAccountCode;
        CostOfSalesAccountCode = costOfSalesAccountCode;
        TaxCode = taxCode;
        IsAvailable = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The menu code.</summary>
    public string Code { get; private set; }

    /// <summary>The menu name.</summary>
    public string Name { get; set; }

    /// <summary>The category (starters, mains, drinks…).</summary>
    public string? Category { get; set; }

    /// <summary>The selling price (tax-exclusive net).</summary>
    public decimal Price { get; set; }

    /// <summary>The revenue account credited.</summary>
    public string RevenueAccountCode { get; private set; }

    /// <summary>The cost-of-sales account debited on the recipe explosion.</summary>
    public string CostOfSalesAccountCode { get; private set; }

    /// <summary>The tax code, if any.</summary>
    public string? TaxCode { get; set; }

    /// <summary>Whether the item is currently available to order.</summary>
    public bool IsAvailable { get; set; }

    /// <summary>The recipe — ingredient stock items and quantities consumed per unit sold.</summary>
    public IReadOnlyList<RecipeLine> Recipe => _recipe.AsReadOnly();

    /// <summary>Adds an ingredient to the recipe.</summary>
    /// <param name="ingredientItemId">The stock item consumed.</param>
    /// <param name="quantity">The quantity consumed per menu item sold.</param>
    /// <returns>The created recipe line.</returns>
    public RecipeLine AddIngredient(long ingredientItemId, decimal quantity)
    {
        RecipeLine line = new(ingredientItemId, quantity);
        _recipe.Add(line);
        return line;
    }

    /// <summary>Alternate prices by price level (e.g. LUNCH, HAPPYHOUR, DELIVERY).</summary>
    public IReadOnlyList<MenuItemPrice> Prices => _prices.AsReadOnly();

    /// <summary>Sets (adds or updates) the price for a named price level.</summary>
    /// <param name="priceLevel">The price level, normalised to upper case.</param>
    /// <param name="price">The price at that level.</param>
    public void SetPrice(string priceLevel, decimal price)
    {
        string level = priceLevel.Trim().ToUpperInvariant();
        MenuItemPrice? existing = _prices.FirstOrDefault(p => p.PriceLevel == level);
        if (existing is null)
        {
            _prices.Add(new MenuItemPrice(level, price));
        }
        else
        {
            existing.SetPrice(price);
        }
    }

    /// <summary>The price for a named price level, or the base price when the level is null/blank or has none.</summary>
    /// <param name="priceLevel">The price level, if any.</param>
    /// <returns>The applicable price.</returns>
    public decimal PriceFor(string? priceLevel)
    {
        if (string.IsNullOrWhiteSpace(priceLevel))
        {
            return Price;
        }

        string level = priceLevel.Trim().ToUpperInvariant();
        return _prices.FirstOrDefault(p => p.PriceLevel == level)?.Price ?? Price;
    }
}

/// <summary>An alternate price for a menu item at a named price level (<c>res.menu_item_price</c>).</summary>
public sealed class MenuItemPrice : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private MenuItemPrice() => PriceLevel = string.Empty;

    /// <summary>Creates a price-level price.</summary>
    /// <param name="priceLevel">The price level (normalised upper case).</param>
    /// <param name="price">The price.</param>
    public MenuItemPrice(string priceLevel, decimal price)
    {
        PriceLevel = priceLevel;
        Price = price;
    }

    /// <summary>The owning menu item id.</summary>
    public long MenuItemId { get; private set; }

    /// <summary>The price level (e.g. LUNCH, HAPPYHOUR, DELIVERY).</summary>
    public string PriceLevel { get; private set; }

    /// <summary>The price at that level.</summary>
    public decimal Price { get; private set; }

    /// <summary>Updates the price.</summary>
    /// <param name="price">The new price.</param>
    public void SetPrice(decimal price) => Price = price;
}

/// <summary>One ingredient of a menu item's recipe (<c>res.recipe_line</c>).</summary>
public sealed class RecipeLine : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RecipeLine()
    {
    }

    /// <summary>Creates a recipe line.</summary>
    /// <param name="ingredientItemId">The stock item consumed.</param>
    /// <param name="quantity">The quantity consumed per menu item sold.</param>
    public RecipeLine(long ingredientItemId, decimal quantity)
    {
        IngredientItemId = ingredientItemId;
        Quantity = quantity;
    }

    /// <summary>The owning menu item id.</summary>
    public long MenuItemId { get; private set; }

    /// <summary>The stock item consumed.</summary>
    public long IngredientItemId { get; private set; }

    /// <summary>The quantity consumed per menu item sold.</summary>
    public decimal Quantity { get; private set; }
}
