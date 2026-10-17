using One4All.Core.Housing;

namespace One4All.Application.Housing;

/// <summary>A due-diligence item as signed off.</summary>
/// <param name="Item">The item.</param>
/// <param name="Acceptable">Whether it is acceptable for the acquisition to proceed.</param>
public sealed record SignedOffItem(DueDiligenceItem Item, bool Acceptable);

/// <summary>One element of a parcel's cost build-up.</summary>
/// <param name="Component">The element.</param>
/// <param name="Amount">Its total.</param>
public sealed record CostElement(AcquisitionCostComponent Component, decimal Amount);

/// <summary>
/// Whether a land acquisition may complete (brief §4.10.1): every due-diligence item signed off as acceptable, a
/// valuation obtained wherever value changes hands, and a purchase price recorded for a purchase - and the cost build-up,
/// element by element, to the total parcel cost. Pure and unit-tested.
/// </summary>
public static class AcquisitionReadiness
{
    /// <summary>What stops an acquisition completing.</summary>
    /// <param name="method">How it is acquired.</param>
    /// <param name="signedOff">The items signed off.</param>
    /// <param name="valued">Whether a valuation has been recorded.</param>
    /// <param name="costs">The costs capitalised.</param>
    /// <returns>The reasons; empty when it may complete.</returns>
    public static IReadOnlyList<string> Problems(AcquisitionMethod method, IReadOnlyList<SignedOffItem> signedOff, bool valued, IReadOnlyList<CostElement> costs)
    {
        ArgumentNullException.ThrowIfNull(signedOff);
        ArgumentNullException.ThrowIfNull(costs);
        List<string> problems = [];
        List<DueDiligenceItem> missing = [.. Enum.GetValues<DueDiligenceItem>().Where(i => !signedOff.Any(s => s.Item == i))];
        if (missing.Count > 0)
        {
            problems.Add($"Not signed off: {string.Join(", ", missing)}.");
        }

        List<DueDiligenceItem> unacceptable = [.. signedOff.Where(s => !s.Acceptable).Select(s => s.Item).Order()];
        if (unacceptable.Count > 0)
        {
            problems.Add($"Not acceptable: {string.Join(", ", unacceptable)}.");
        }

        if (method is AcquisitionMethod.Purchase or AcquisitionMethod.Compulsory or AcquisitionMethod.LandSwap && !valued)
        {
            problems.Add("No valuation obtained.");
        }

        if (method == AcquisitionMethod.Purchase && !costs.Any(c => c.Component == AcquisitionCostComponent.PurchasePrice))
        {
            problems.Add("No purchase price recorded.");
        }

        return problems;
    }

    /// <summary>The cost build-up: each element's total, in build-up order, without empty elements.</summary>
    /// <param name="costs">The costs capitalised.</param>
    /// <returns>The elements.</returns>
    public static IReadOnlyList<CostElement> BuildUp(IEnumerable<CostElement> costs)
    {
        ArgumentNullException.ThrowIfNull(costs);
        return [.. costs.GroupBy(c => c.Component).OrderBy(g => g.Key).Select(g => new CostElement(g.Key, g.Sum(c => c.Amount))).Where(e => e.Amount != 0m)];
    }
}
