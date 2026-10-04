namespace One4All.Application.Housing;

/// <summary>What an enumeration survey found about a household.</summary>
/// <param name="HeadDateOfBirth">The head of household's date of birth.</param>
/// <param name="OccupiedSince">Since when the structure has been occupied.</param>
/// <param name="EnumeratedOn">When the household was counted.</param>
/// <param name="HoldsCouncilProperty">Whether the head already holds a council stand or owns property on the register.</param>
public sealed record HouseholdFacts(DateOnly HeadDateOfBirth, DateOnly OccupiedSince, DateOnly EnumeratedOn, bool HoldsCouncilProperty);

/// <summary>
/// Eligibility of an enumerated informal-settlement household for regularisation (brief §4.10.8): the structure must have
/// been occupied on or before the settlement's cut-off date, the head of household must be an adult, and must not already
/// hold council land or property. Pure and unit-tested.
/// </summary>
public static class RegularisationRules
{
    /// <summary>Why a household does not qualify.</summary>
    /// <param name="facts">The household.</param>
    /// <param name="cutOffDate">The settlement's cut-off date.</param>
    /// <param name="minimumAge">The minimum age of a head of household.</param>
    /// <returns>The reasons; empty when it qualifies.</returns>
    public static IReadOnlyList<string> Problems(HouseholdFacts facts, DateOnly cutOffDate, int minimumAge)
    {
        ArgumentNullException.ThrowIfNull(facts);
        List<string> problems = [];
        if (facts.OccupiedSince > cutOffDate)
        {
            problems.Add($"Occupied from {facts.OccupiedSince:yyyy-MM-dd}, after the cut-off date {cutOffDate:yyyy-MM-dd}.");
        }

        int age = WaitingListRules.FullYears(facts.HeadDateOfBirth, facts.EnumeratedOn);
        if (age < minimumAge)
        {
            problems.Add($"The head of household is {age}, under the minimum age of {minimumAge}.");
        }

        if (facts.HoldsCouncilProperty)
        {
            problems.Add("The head of household already holds council land or property.");
        }

        return problems;
    }
}
