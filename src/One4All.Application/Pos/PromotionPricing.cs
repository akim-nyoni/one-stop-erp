using One4All.Application.Abstractions.Pos;
using One4All.Core.Pos;

namespace One4All.Application.Pos;

/// <summary>An item a promotion names, and its group (0: qualifying; 1: reward; a meal deal's courses from 1).</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Group">The group.</param>
public sealed record PromotionItemRef(long ItemId, int Group);

/// <summary>
/// A promotion as the till prices it - everything <see cref="PromotionPricing"/> needs, detached from the database so the offline snapshot can carry it
/// (brief §6B: offline sales keep their promotions).
/// </summary>
public sealed record PromotionDefinition(
    string Code, string Name, PromotionKind Kind, int Priority, bool Compounding, int Quantity, int RewardQuantity, decimal? Price, decimal? Percent, string? Category,
    bool MemberOnly, string? CustomerTypes, string? CouponCode, DateOnly StartsOn, DateOnly? EndsOn, TimeOnly? DailyFrom, TimeOnly? DailyTo, long? BranchId,
    string DiscountAccountCode, IReadOnlyList<PromotionItemRef> Items)
{
    /// <summary>The definition of a stored promotion.</summary>
    /// <param name="p">The promotion (with its items).</param>
    /// <returns>The definition.</returns>
    public static PromotionDefinition From(Promotion p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new(p.Code, p.Name, p.Kind, p.Priority, p.Compounding, p.Quantity, p.RewardQuantity, p.Price, p.Percent, p.Category, p.MemberOnly, p.CustomerTypes, p.CouponCode,
            p.StartsOn, p.EndsOn, p.DailyFrom, p.DailyTo, p.BranchId, p.DiscountAccountCode, [.. p.Items.Select(i => new PromotionItemRef(i.ItemId, i.Group))]);
    }

    /// <summary>Whether it runs for a basket: its dates, branch, happy-hour window, members, customer type and coupon.</summary>
    /// <param name="request">The basket.</param>
    /// <param name="coupons">The coupons entered (upper case).</param>
    /// <returns>True when it applies.</returns>
    public bool RunsFor(BasketRequest request, IReadOnlySet<string> coupons)
    {
        ArgumentNullException.ThrowIfNull(request);
        DateOnly date = DateOnly.FromDateTime(request.At);
        TimeOnly time = TimeOnly.FromDateTime(request.At);
        bool inWindow = (DailyFrom, DailyTo) switch
        {
            (TimeOnly from, TimeOnly to) when from <= to => time >= from && time < to,
            (TimeOnly from, TimeOnly to) => time >= from || time < to,     // a happy hour across midnight
            _ => true,
        };
        bool forCustomer = CustomerTypes is null || (request.CustomerType is string type && CustomerTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(type.Trim(), StringComparer.OrdinalIgnoreCase));
        return StartsOn <= date && (EndsOn is null || EndsOn >= date) && (BranchId is null || BranchId == request.BranchId) && inWindow && (!MemberOnly || request.IsMember)
            && forCustomer && (CouponCode is null || coupons.Contains(CouponCode.ToUpperInvariant()));
    }
}

/// <summary>Prices a basket against the promotions that run for it (the same rules online and offline). Pure.</summary>
public static class PromotionPricing
{
    /// <summary>Prices the basket.</summary>
    /// <param name="promotions">The active promotions.</param>
    /// <param name="request">The basket.</param>
    /// <param name="itemCategory">Each basket item's category (for category promotions).</param>
    /// <returns>The priced basket and the promotions applied.</returns>
    public static BasketPricing Price(IEnumerable<PromotionDefinition> promotions, BasketRequest request, IReadOnlyDictionary<long, string?> itemCategory)
    {
        ArgumentNullException.ThrowIfNull(request);
        HashSet<string> coupons = new(request.Coupons.Select(c => c.Trim().ToUpperInvariant()));
        List<PromotionDefinition> running = [.. promotions.Where(p => p.RunsFor(request, coupons))];
        List<PromotionRule> rules = [.. running.Select(p => new PromotionRule(p.Code, p.Kind, p.Priority, p.Compounding, p.Quantity, p.RewardQuantity, p.Price, p.Percent,
            new HashSet<long>(p.Items.Where(i => i.Group == 0).Select(i => i.ItemId)
                .Concat(p.Category is null ? [] : itemCategory.Where(c => string.Equals(c.Value, p.Category, StringComparison.OrdinalIgnoreCase)).Select(c => c.Key))),
            p.Kind == PromotionKind.BuyXGetY ? new HashSet<long>(p.Items.Where(i => i.Group == 1).Select(i => i.ItemId)) : [],
            p.Kind == PromotionKind.MealDeal ? [.. p.Items.Where(i => i.Group > 0).GroupBy(i => i.Group).OrderBy(g => g.Key).Select(g => (IReadOnlySet<long>)new HashSet<long>(g.Select(i => i.ItemId)))] : [],
            p.DiscountAccountCode))];

        IReadOnlyList<LineDiscount> discounts = PromotionEngine.Apply(request.Lines.Select((l, i) => new BasketLine(i, l.ItemId, l.Quantity, l.UnitPrice)), rules);
        List<PricedLine> lines = [.. request.Lines.Select((l, i) =>
        {
            List<LineDiscount> mine = [.. discounts.Where(d => d.Line == i)];
            return new PricedLine(l.ItemId, l.Quantity, decimal.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero), mine.Sum(d => d.Amount), [.. mine.Select(d => d.PromotionCode)],
                mine.FirstOrDefault()?.DiscountAccountCode);
        })];
        Dictionary<string, string> names = running.ToDictionary(p => p.Code, p => p.Name);
        return new BasketPricing(lines, [.. discounts.GroupBy(d => d.PromotionCode).Select(g => (g.Key, names[g.Key], g.Sum(d => d.Amount)))]);
    }
}
