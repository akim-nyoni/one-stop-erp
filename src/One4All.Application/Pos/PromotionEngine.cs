using One4All.Core.Pos;

namespace One4All.Application.Pos;

/// <summary>A promotion as the engine applies it (items already resolved, the category folded into the qualifying set).</summary>
/// <param name="Code">The code.</param>
/// <param name="Kind">The kind.</param>
/// <param name="Priority">The order it is applied in (lower first).</param>
/// <param name="Compounding">Whether it may stack on units already discounted.</param>
/// <param name="Quantity">N (N for price) or X (buy X get Y).</param>
/// <param name="RewardQuantity">Y (buy X get Y).</param>
/// <param name="Price">The set price.</param>
/// <param name="Percent">The percentage off.</param>
/// <param name="Qualifying">The qualifying items.</param>
/// <param name="Reward">The reward items (buy X get Y; empty = from the qualifying items).</param>
/// <param name="Courses">The meal-deal courses.</param>
/// <param name="DiscountAccountCode">The discount GL account.</param>
public sealed record PromotionRule(string Code, PromotionKind Kind, int Priority, bool Compounding, int Quantity, int RewardQuantity, decimal? Price, decimal? Percent,
    IReadOnlySet<long> Qualifying, IReadOnlySet<long> Reward, IReadOnlyList<IReadOnlySet<long>> Courses, string DiscountAccountCode);

/// <summary>A basket line to price.</summary>
/// <param name="Line">The line number.</param>
/// <param name="ItemId">The item.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="UnitPrice">The unit price before promotions.</param>
public sealed record BasketLine(int Line, long ItemId, decimal Quantity, decimal UnitPrice);

/// <summary>A promotion's discount on a basket line.</summary>
/// <param name="Line">The line number.</param>
/// <param name="PromotionCode">The promotion.</param>
/// <param name="DiscountAccountCode">The discount GL account.</param>
/// <param name="Amount">The discount.</param>
public sealed record LineDiscount(int Line, string PromotionCode, string DiscountAccountCode, decimal Amount);

/// <summary>
/// The promotions engine (brief §4.1), pure: applies the active promotions to a basket in priority order. Count-based promotions (N for
/// price, buy X get Y, meal deals) work on whole units, most expensive first into sets, cheapest first for rewards; percent-off covers
/// every unit, weighed fractions included. A non-compounding promotion only uses units no earlier promotion has claimed; a compounding
/// one stacks on whatever price is left.
/// </summary>
public static class PromotionEngine
{
    /// <summary>Applies the promotions.</summary>
    /// <param name="lines">The basket.</param>
    /// <param name="rules">The active promotions.</param>
    /// <returns>Each line's discount per promotion.</returns>
    public static IReadOnlyList<LineDiscount> Apply(IEnumerable<BasketLine> lines, IEnumerable<PromotionRule> rules)
    {
        List<Unit> units = [];
        foreach (BasketLine line in lines.Where(l => l.Quantity > 0m && l.UnitPrice > 0m))
        {
            int whole = (int)decimal.Floor(line.Quantity);
            for (int i = 0; i < whole; i++)
            {
                units.Add(new Unit(line.Line, line.ItemId, line.UnitPrice, true));
            }

            decimal fraction = line.Quantity - whole;
            if (fraction > 0m)
            {
                units.Add(new Unit(line.Line, line.ItemId, line.UnitPrice * fraction, false));
            }
        }

        Dictionary<(int Line, string Code), (string Account, decimal Amount)> discounts = [];
        foreach (PromotionRule rule in rules.OrderBy(r => r.Priority).ThenBy(r => r.Code, StringComparer.Ordinal))
        {
            List<(Unit Unit, decimal Discount)> applied = rule.Kind switch
            {
                PromotionKind.NForPrice => Sets(Available(units, rule, rule.Qualifying, wholeOnly: true), rule.Quantity, rule.Price),
                PromotionKind.BuyXGetY => BuyGet(units, rule),
                PromotionKind.PercentOff => [.. Available(units, rule, rule.Qualifying, wholeOnly: false).Select(u => (u, u.Remaining * (rule.Percent ?? 0m) / 100m))],
                PromotionKind.MealDeal => Meals(units, rule),
                _ => [],
            };

            foreach ((Unit unit, decimal discount) in applied)
            {
                decimal amount = decimal.Round(Math.Min(discount, unit.Remaining), 2, MidpointRounding.AwayFromZero);
                unit.Discount += amount;
                unit.Claimed |= !rule.Compounding;
                (string Account, decimal Amount) so = discounts.GetValueOrDefault((unit.Line, rule.Code), (rule.DiscountAccountCode, 0m));
                discounts[(unit.Line, rule.Code)] = (so.Account, so.Amount + amount);
            }
        }

        return [.. discounts.Where(d => d.Value.Amount > 0m).OrderBy(d => d.Key.Line).ThenBy(d => d.Key.Code, StringComparer.Ordinal)
            .Select(d => new LineDiscount(d.Key.Line, d.Key.Code, d.Value.Account, d.Value.Amount))];
    }

    private static List<Unit> Available(List<Unit> units, PromotionRule rule, IReadOnlySet<long> items, bool wholeOnly)
        => [.. units.Where(u => items.Contains(u.ItemId) && (rule.Compounding || !u.Claimed) && (!wholeOnly || u.Whole) && u.Remaining > 0m)];

    // Units grouped into sets of n (most expensive first), each set sold for the price: the saving shared across the set by value.
    private static List<(Unit, decimal)> Sets(List<Unit> candidates, int n, decimal? price)
    {
        List<(Unit, decimal)> result = [];
        if (n <= 0 || price is not decimal setPrice)
        {
            return result;
        }

        List<Unit> ordered = [.. candidates.OrderByDescending(u => u.Remaining)];
        for (int start = 0; start + n <= ordered.Count; start += n)
        {
            result.AddRange(Share(ordered.GetRange(start, n), setPrice));
        }

        return result;
    }

    private static List<(Unit, decimal)> BuyGet(List<Unit> units, PromotionRule rule)
    {
        List<(Unit, decimal)> result = [];
        decimal percent = rule.Percent ?? 100m;
        if (rule.Quantity <= 0 || rule.RewardQuantity <= 0)
        {
            return result;
        }

        List<Unit> qualifying = [.. Available(units, rule, rule.Qualifying, wholeOnly: true).OrderByDescending(u => u.Remaining)];
        if (rule.Reward.Count == 0)
        {
            // Lowest price free: in each set of X + Y the cheapest Y are discounted.
            int size = rule.Quantity + rule.RewardQuantity;
            for (int start = 0; start + size <= qualifying.Count; start += size)
            {
                List<Unit> set = qualifying.GetRange(start, size);
                result.AddRange(set.Take(rule.Quantity).Select(u => (u, 0m)));
                result.AddRange(set.Skip(rule.Quantity).Select(u => (u, u.Remaining * percent / 100m)));
            }

            return result;
        }

        int sets = qualifying.Count / rule.Quantity;
        List<Unit> rewards = [.. Available(units, rule, rule.Reward, wholeOnly: true).Except(qualifying).OrderBy(u => u.Remaining).Take(sets * rule.RewardQuantity)];
        int used = (int)Math.Ceiling(rewards.Count / (decimal)rule.RewardQuantity) * rule.Quantity;
        result.AddRange(qualifying.Take(used).Select(u => (u, 0m)));
        result.AddRange(rewards.Select(u => (u, u.Remaining * percent / 100m)));
        return result;
    }

    // One unit from each course (most expensive first) for the deal price.
    private static List<(Unit, decimal)> Meals(List<Unit> units, PromotionRule rule)
    {
        List<(Unit, decimal)> result = [];
        if (rule.Courses.Count == 0 || rule.Price is not decimal price)
        {
            return result;
        }

        List<List<Unit>> courses = [.. rule.Courses.Select(c => Available(units, rule, c, wholeOnly: true).OrderByDescending(u => u.Remaining).ToList())];
        HashSet<Unit> taken = [];
        int meals = courses.Min(c => c.Count);
        for (int i = 0; i < meals; i++)
        {
            List<Unit> meal = [.. courses.Select(c => c.First(u => !taken.Contains(u)))];
            if (meal.Distinct().Count() != meal.Count)
            {
                break;
            }

            taken.UnionWith(meal);
            result.AddRange(Share(meal, price));
        }

        return result;
    }

    // The saving (total − price, never negative) shared across the units by value, the rounding left on the last.
    private static IEnumerable<(Unit, decimal)> Share(List<Unit> set, decimal price)
    {
        decimal total = set.Sum(u => u.Remaining);
        decimal saving = Math.Max(0m, total - price);
        decimal given = 0m;
        for (int i = 0; i < set.Count; i++)
        {
            decimal part = i == set.Count - 1 ? saving - given : decimal.Round(saving * set[i].Remaining / total, 2, MidpointRounding.AwayFromZero);
            given += part;
            yield return (set[i], part);
        }
    }

    private sealed class Unit(int line, long itemId, decimal price, bool whole)
    {
        public int Line { get; } = line;

        public long ItemId { get; } = itemId;

        public bool Whole { get; } = whole;

        public decimal Discount { get; set; }

        public bool Claimed { get; set; }

        public decimal Remaining => price - Discount;
    }
}
