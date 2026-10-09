using One4All.Core.Common;
using One4All.Core.Pos;

namespace One4All.Application.Abstractions.Pos;

/// <summary>A promotion to save (created, or replaced by code).</summary>
public sealed record PromotionRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the code.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Gets the name shown on receipts.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the kind.</summary>
    public PromotionKind Kind { get; init; }

    /// <summary>Gets the discount GL account.</summary>
    public string DiscountAccountCode { get; init; } = string.Empty;

    /// <summary>Gets N (N for price) or X (buy X get Y).</summary>
    public int Quantity { get; init; }

    /// <summary>Gets Y (buy X get Y).</summary>
    public int RewardQuantity { get; init; }

    /// <summary>Gets the set price.</summary>
    public decimal? Price { get; init; }

    /// <summary>Gets the percentage off.</summary>
    public decimal? Percent { get; init; }

    /// <summary>Gets the category a percent-off covers.</summary>
    public string? Category { get; init; }

    /// <summary>Gets the qualifying item codes.</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>Gets the reward item codes (buy X get Y).</summary>
    public IReadOnlyList<string> RewardItems { get; init; } = [];

    /// <summary>Gets the meal-deal courses (item codes per course).</summary>
    public IReadOnlyList<IReadOnlyList<string>> Courses { get; init; } = [];

    /// <summary>Gets the priority.</summary>
    public int Priority { get; init; } = 10;

    /// <summary>Gets a value indicating whether it compounds.</summary>
    public bool Compounding { get; init; }

    /// <summary>Gets a value indicating whether it is for members only.</summary>
    public bool MemberOnly { get; init; }

    /// <summary>Gets the customer types it is for (empty: every customer).</summary>
    public IReadOnlyList<string> CustomerTypes { get; init; } = [];

    /// <summary>Gets the coupon code it needs.</summary>
    public string? CouponCode { get; init; }

    /// <summary>Gets the first day.</summary>
    public DateOnly StartsOn { get; init; }

    /// <summary>Gets the last day.</summary>
    public DateOnly? EndsOn { get; init; }

    /// <summary>Gets the daily happy-hour start.</summary>
    public TimeOnly? DailyFrom { get; init; }

    /// <summary>Gets the daily happy-hour end.</summary>
    public TimeOnly? DailyTo { get; init; }

    /// <summary>Gets the branch it runs in.</summary>
    public long? BranchId { get; init; }
}

/// <summary>A basket line to price.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="UnitPrice">The unit price before promotions.</param>
public readonly record struct BasketItem(long ItemId, decimal Quantity, decimal UnitPrice);

/// <summary>A basket to price.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="BranchId">The branch selling.</param>
/// <param name="At">When.</param>
/// <param name="IsMember">Whether the customer is a loyalty member.</param>
/// <param name="Coupons">Coupon codes presented.</param>
/// <param name="Lines">The lines.</param>
public sealed record BasketRequest(long CompanyId, long? BranchId, DateTime At, bool IsMember, IReadOnlyList<string> Coupons, IReadOnlyList<BasketItem> Lines, string? CustomerType = null);

/// <summary>A priced basket line: its promotion discount, the promotion and the account it posts to.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="Gross">Quantity × unit price.</param>
/// <param name="Discount">The promotion discount.</param>
/// <param name="PromotionCodes">The promotions that discounted it.</param>
/// <param name="DiscountAccountCode">The discount account (the first promotion's).</param>
public sealed record PricedLine(long ItemId, decimal Quantity, decimal Gross, decimal Discount, IReadOnlyList<string> PromotionCodes, string? DiscountAccountCode)
{
    /// <summary>Gets the net after promotions.</summary>
    public decimal Net => Gross - Discount;
}

/// <summary>A priced basket.</summary>
/// <param name="Lines">The lines, in basket order.</param>
/// <param name="Applied">Each promotion applied and its saving (for the receipt).</param>
public sealed record BasketPricing(IReadOnlyList<PricedLine> Lines, IReadOnlyList<(string Code, string Name, decimal Saving)> Applied);

/// <summary>
/// The POS promotions engine (brief §4.1): promotions configured as data — mix and match, buy X get Y, lowest price free, percent off a
/// category, meal deals — run by date, happy-hour window, branch, membership and coupon, applied by priority with compounding, and a
/// basket priced with each line's discount and the account it posts to (the sale posts gross revenue and the discount separately).
/// </summary>
public interface IPromotionService
{
    /// <summary>Saves a promotion, refused when its kind's parameters are missing (<c>Pos.PromotionInvalid</c>) or an item is unknown.</summary>
    /// <param name="request">The promotion.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The promotion id.</returns>
    Task<Result<long>> SaveAsync(PromotionRequest request, CancellationToken ct = default);

    /// <summary>Prices a basket with the promotions running for it.</summary>
    /// <param name="request">The basket.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The priced basket.</returns>
    Task<BasketPricing> PriceBasketAsync(BasketRequest request, CancellationToken ct = default);
}
