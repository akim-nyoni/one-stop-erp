using One4All.Core.Housing;

namespace One4All.Application.Housing;

/// <summary>
/// Land register rules (brief §14 H6–H8): the land-bank cost leaving with part of a parcel is pro rata to area (all of it
/// when the whole parcel goes, so no rounding residue is left behind); a dispute only moves forward through its stages
/// and nothing follows resolution. Pure and unit-tested.
/// </summary>
public static class LandRegisterRules
{
    /// <summary>The cost of part of a parcel.</summary>
    /// <param name="parcelArea">The parcel's remaining area.</param>
    /// <param name="parcelCost">Its remaining cost.</param>
    /// <param name="area">The area leaving.</param>
    /// <returns>The cost leaving with it, to the cent.</returns>
    public static decimal CostOfArea(decimal parcelArea, decimal parcelCost, decimal area)
        => area >= parcelArea ? parcelCost : Math.Round(parcelCost * area / parcelArea, 2, MidpointRounding.AwayFromZero);

    /// <summary>Whether a dispute may move from one stage to another.</summary>
    /// <param name="from">Its stage.</param>
    /// <param name="to">The proposed stage.</param>
    /// <returns>True for a later stage of an unresolved dispute.</returns>
    public static bool CanMove(LandDisputeStage from, LandDisputeStage to)
        => from != LandDisputeStage.Resolved && to > from && Enum.IsDefined(to);

    /// <summary>The age of a dispute in days, as at a date (to resolution when resolved).</summary>
    /// <param name="openedOn">When it was opened.</param>
    /// <param name="resolvedOn">When it was resolved, if it has been.</param>
    /// <param name="asAt">The date.</param>
    /// <returns>Days open.</returns>
    public static int AgeInDays(DateOnly openedOn, DateOnly? resolvedOn, DateOnly asAt)
        => Math.Max(0, (resolvedOn is DateOnly r && r < asAt ? r : asAt).DayNumber - openedOn.DayNumber);
}
