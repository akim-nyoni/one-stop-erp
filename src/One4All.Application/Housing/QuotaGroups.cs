using One4All.Core.Housing;

namespace One4All.Application.Housing;

/// <summary>
/// Beneficiary-group membership and quota compliance (brief §14 H37): an applicant is in the groups they declared, plus
/// Youth (18 to 35) and Elderly (60 and over) by age on the allocation date and Disabled from the disability flag; a
/// group's actual share is its members among the allocations, met when it reaches the target. Pure and unit-tested.
/// </summary>
public static class QuotaGroups
{
    /// <summary>The oldest age counted as youth.</summary>
    public const int YouthMaximumAge = 35;

    /// <summary>The age from which an applicant counts as elderly.</summary>
    public const int ElderlyMinimumAge = 60;

    /// <summary>The individual groups, in order.</summary>
    public static IReadOnlyList<BeneficiaryGroups> All { get; } = [.. Enum.GetValues<BeneficiaryGroups>().Where(g => g != BeneficiaryGroups.None)];

    /// <summary>The groups an applicant belongs to on a date.</summary>
    /// <param name="declared">The groups declared and proved.</param>
    /// <param name="hasDisability">The disability flag.</param>
    /// <param name="dateOfBirth">The date of birth, if captured.</param>
    /// <param name="asAt">The date (usually the allocation's).</param>
    /// <returns>The groups.</returns>
    public static BeneficiaryGroups Of(BeneficiaryGroups declared, bool hasDisability, DateOnly? dateOfBirth, DateOnly asAt)
    {
        BeneficiaryGroups groups = declared;
        if (hasDisability)
        {
            groups |= BeneficiaryGroups.Disabled;
        }

        if (dateOfBirth is DateOnly born)
        {
            int age = asAt.Year - born.Year - (asAt < born.AddYears(asAt.Year - born.Year) ? 1 : 0);
            if (age is >= 18 and <= YouthMaximumAge)
            {
                groups |= BeneficiaryGroups.Youth;
            }

            if (age >= ElderlyMinimumAge)
            {
                groups |= BeneficiaryGroups.Elderly;
            }
        }

        return groups;
    }

    /// <summary>A group's actual share of allocations, in percent to one decimal (zero when there are none).</summary>
    /// <param name="members">Allocations to the group's members.</param>
    /// <param name="allocations">All allocations.</param>
    /// <returns>The share.</returns>
    public static decimal Share(int members, int allocations) => allocations == 0 ? 0m : Math.Round(members * 100m / allocations, 1, MidpointRounding.AwayFromZero);
}
