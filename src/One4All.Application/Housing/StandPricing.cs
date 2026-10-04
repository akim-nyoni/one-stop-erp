using One4All.Core.Housing;

namespace One4All.Application.Housing;

/// <summary>The terms of a pricing rule.</summary>
/// <param name="OverheadPercent">Overhead / administration recovery on cost.</param>
/// <param name="LevyPerStand">Development levy per stand.</param>
/// <param name="LevyPerSquareMetre">Development levy per square metre.</param>
/// <param name="MarginPercent">Margin / cost-recovery factor on cost plus overhead.</param>
/// <param name="RoundTo">The price is rounded up to a multiple of this (0 = to the cent).</param>
public sealed record PricingTerms(decimal OverheadPercent, decimal LevyPerStand, decimal LevyPerSquareMetre, decimal MarginPercent, decimal RoundTo);

/// <summary>A stand's price, built up.</summary>
/// <param name="Cost">Land and servicing cost (the stand's capitalised cost).</param>
/// <param name="Overhead">Overhead / administration recovery.</param>
/// <param name="Levy">Development levy.</param>
/// <param name="Margin">Margin / cost recovery, including rounding.</param>
/// <param name="Price">The selling price.</param>
public sealed record PriceBuildUp(decimal Cost, decimal Overhead, decimal Levy, decimal Margin, decimal Price);

/// <summary>An approved pricing rule as the resolver sees it.</summary>
/// <param name="Id">The rule.</param>
/// <param name="Scope">What it prices.</param>
/// <param name="SchemeId">The scheme (scheme and size-band rules).</param>
/// <param name="MinSize">The smallest stand in the band.</param>
/// <param name="MaxSize">The largest stand in the band.</param>
/// <param name="StandId">The stand (stand rules).</param>
/// <param name="EffectiveFrom">When it takes effect.</param>
public sealed record PricingRuleKey(long Id, PricingScope Scope, long? SchemeId, decimal? MinSize, decimal? MaxSize, long? StandId, DateOnly EffectiveFrom);

/// <summary>
/// Stand selling prices (brief §4.10.2): land cost + servicing cost + overhead recovery + development levy + margin =
/// price, and which approved rule prices a stand on a date — a stand's own rule before its size band's before its
/// scheme's, the latest effective one in that scope. Pure and unit-tested.
/// </summary>
public static class StandPricing
{
    /// <summary>Builds a stand's price.</summary>
    /// <param name="cost">The stand's capitalised land and servicing cost.</param>
    /// <param name="sizeSquareMetres">Its size.</param>
    /// <param name="terms">The rule's terms.</param>
    /// <returns>The build-up.</returns>
    public static PriceBuildUp Build(decimal cost, decimal sizeSquareMetres, PricingTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        decimal overhead = Math.Round(cost * terms.OverheadPercent / 100m, 2, MidpointRounding.AwayFromZero);
        decimal levy = Math.Round(terms.LevyPerStand + (terms.LevyPerSquareMetre * sizeSquareMetres), 2, MidpointRounding.AwayFromZero);
        decimal margin = Math.Round((cost + overhead) * terms.MarginPercent / 100m, 2, MidpointRounding.AwayFromZero);
        decimal price = cost + overhead + levy + margin;
        if (terms.RoundTo > 0m)
        {
            decimal rounded = Math.Ceiling(price / terms.RoundTo) * terms.RoundTo;
            margin += rounded - price;
            price = rounded;
        }

        return new PriceBuildUp(cost, overhead, levy, margin, price);
    }

    /// <summary>The approved rule that prices a stand on a date, or null when none does.</summary>
    /// <param name="rules">The approved rules.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="schemeId">Its scheme.</param>
    /// <param name="sizeSquareMetres">Its size.</param>
    /// <param name="date">The date.</param>
    /// <returns>The rule.</returns>
    public static PricingRuleKey? Choose(IEnumerable<PricingRuleKey> rules, long standId, long? schemeId, decimal sizeSquareMetres, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(rules);
        List<PricingRuleKey> applicable = [.. rules.Where(r => r.EffectiveFrom <= date && r.Scope switch
        {
            PricingScope.Stand => r.StandId == standId,
            PricingScope.SizeBand => r.SchemeId == schemeId && sizeSquareMetres >= r.MinSize && sizeSquareMetres <= r.MaxSize,
            PricingScope.Scheme => r.SchemeId == schemeId,
            _ => false,
        })];
        return applicable.OrderBy(r => r.Scope).ThenByDescending(r => r.EffectiveFrom).ThenByDescending(r => r.Id).FirstOrDefault();
    }
}
