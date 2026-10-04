using One4All.Core.Housing;

namespace One4All.Application.Housing;

/// <summary>An IAS 2 lower-of-cost-and-NRV assessment of one item of land stock.</summary>
/// <param name="CostsToSell">Selling costs at the given rate.</param>
/// <param name="NetRealisableValue">Estimated price less costs to complete and to sell.</param>
/// <param name="WriteDown">Carrying amount above NRV (zero when NRV covers it).</param>
public sealed record NrvAssessment(decimal CostsToSell, decimal NetRealisableValue, decimal WriteDown);

/// <summary>
/// Scheme progress rules (brief §14 H9, H12, H13): the IAS 2 test of land stock — NRV is the estimated selling price
/// less the costs to complete and the costs to sell, and any carrying amount above it is the write-down; a stand is
/// serviced only once every bulk service reaches it; survey stages only move forward. Pure and unit-tested.
/// </summary>
public static class SchemeProgressRules
{
    /// <summary>Assesses one item.</summary>
    /// <param name="carrying">Its carrying amount (cost).</param>
    /// <param name="estimatedPrice">The estimated selling price.</param>
    /// <param name="costToComplete">The cost still to be spent before it can be sold.</param>
    /// <param name="costToSellPercent">Selling costs as a percentage of price.</param>
    /// <returns>The assessment, to the cent.</returns>
    public static NrvAssessment Assess(decimal carrying, decimal estimatedPrice, decimal costToComplete, decimal costToSellPercent)
    {
        decimal costsToSell = Math.Round(estimatedPrice * costToSellPercent / 100m, 2, MidpointRounding.AwayFromZero);
        decimal nrv = estimatedPrice - costToComplete - costsToSell;
        return new NrvAssessment(costsToSell, nrv, Math.Max(0m, carrying - nrv));
    }

    /// <summary>How many of a scheme's stands are serviced: the fewest any of the bulk services reaches (none until all have started).</summary>
    /// <param name="standsReached">Stands reached by each service with a certificate.</param>
    /// <param name="stands">The scheme's stands.</param>
    /// <returns>The serviced count.</returns>
    public static int ServicedStands(IReadOnlyDictionary<BulkService, int> standsReached, int stands)
        => Enum.GetValues<BulkService>().All(standsReached.ContainsKey) ? Math.Min(stands, Enum.GetValues<BulkService>().Min(s => standsReached[s])) : 0;

    /// <summary>Whether a stand may move to a survey stage.</summary>
    /// <param name="current">Its stage, if surveyed at all.</param>
    /// <param name="next">The stage proposed.</param>
    /// <returns>True for a later stage.</returns>
    public static bool CanAdvance(SurveyStage? current, SurveyStage next) => Enum.IsDefined(next) && (current is null || next > current);
}
