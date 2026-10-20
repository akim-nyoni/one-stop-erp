using One4All.Core.Housing;

namespace One4All.Application.Housing;

/// <summary>What the qualification rules and priority score look at.</summary>
/// <param name="DateOfBirth">The date of birth.</param>
/// <param name="ResidentSince">Since when resident in the area.</param>
/// <param name="RegistrationDate">When registered on the list (seniority).</param>
/// <param name="Dependants">The number of dependants.</param>
/// <param name="HasDisability">Whether the applicant has a disability.</param>
/// <param name="MonthlyIncome">The monthly income.</param>
/// <param name="Tenure">Where the applicant lives now.</param>
/// <param name="CooperativeMember">Whether the applicant belongs to a registered housing cooperative.</param>
public sealed record ApplicantFacts(DateOnly DateOfBirth, DateOnly ResidentSince, DateOnly RegistrationDate, int Dependants, bool HasDisability, decimal MonthlyIncome, CurrentTenure Tenure,
    bool CooperativeMember = false);

/// <summary>The configurable qualification rules beyond age, residency and income (brief §4.10.3).</summary>
/// <param name="FirstTimeBuyersOnly">Only those who have never held a stand or owned property qualify.</param>
/// <param name="RefuseForfeited">Those whose earlier stand was repossessed or agreement cancelled do not qualify.</param>
/// <param name="RequireRatesClearance">Applicants must be clear of overdue rates and charges.</param>
/// <param name="CategoryIncomeCeilings">The income ceiling per category.</param>
/// <param name="RentalFromWaitingList">Council rental units are let only to applicants on the rental waiting list.</param>
public sealed record QualificationRules(bool FirstTimeBuyersOnly, bool RefuseForfeited, bool RequireRatesClearance, IReadOnlyDictionary<HousingCategory, decimal> CategoryIncomeCeilings,
    bool RentalFromWaitingList = false)
{
    /// <summary>No further rules.</summary>
    public static QualificationRules None { get; } = new(false, false, false, new Dictionary<HousingCategory, decimal>());
}

/// <summary>What the further qualification rules look at for one household.</summary>
/// <param name="Category">The category applied for.</param>
/// <param name="MonthlyIncome">The monthly income.</param>
/// <param name="EverHeldProperty">Whether the household has ever held a stand or owned property in the area.</param>
/// <param name="Forfeited">Whether an earlier stand was repossessed or an agreement cancelled.</param>
/// <param name="OverdueCharges">What the household owes the council past its due date.</param>
public sealed record QualificationFacts(HousingCategory Category, decimal MonthlyIncome, bool EverHeldProperty, bool Forfeited, decimal OverdueCharges);

/// <summary>The qualification thresholds and score weights in force.</summary>
/// <param name="MinimumAge">The minimum age.</param>
/// <param name="MinimumResidencyYears">The minimum years resident.</param>
/// <param name="MaximumMonthlyIncome">The income ceiling, or null for none.</param>
/// <param name="PointsPerYearOnList">Points per full year on the list.</param>
/// <param name="PointsPerYearResident">Points per full year resident.</param>
/// <param name="PointsPerDependant">Points per dependant.</param>
/// <param name="DisabilityPoints">Points for a disability.</param>
/// <param name="HomelessPoints">Points for being homeless.</param>
/// <param name="LodgerPoints">Points for lodging or staying with relatives.</param>
/// <param name="PointsPerYearOfAge">Points per full year of age above <paramref name="AgePointsFromYears"/>.</param>
/// <param name="AgePointsFromYears">The age age points start from.</param>
/// <param name="CooperativeMemberPoints">Points for a member of a registered housing cooperative.</param>
public sealed record WaitingListTerms(int MinimumAge, int MinimumResidencyYears, decimal? MaximumMonthlyIncome, decimal PointsPerYearOnList, decimal PointsPerYearResident,
    decimal PointsPerDependant, decimal DisabilityPoints, decimal HomelessPoints, decimal LodgerPoints, decimal PointsPerYearOfAge = 0m, int AgePointsFromYears = 0,
    decimal CooperativeMemberPoints = 0m)
{
    /// <summary>The terms of a council's stored policy.</summary>
    /// <param name="policy">The policy.</param>
    /// <returns>The terms.</returns>
    public static WaitingListTerms From(WaitingListPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new(policy.MinimumAge, policy.MinimumResidencyYears, policy.MaximumMonthlyIncome, policy.PointsPerYearOnList, policy.PointsPerYearResident,
            policy.PointsPerDependant, policy.DisabilityPoints, policy.HomelessPoints, policy.LodgerPoints, policy.PointsPerYearOfAge, policy.AgePointsFromYears,
            policy.CooperativeMemberPoints);
    }
}

/// <summary>One line of a score explanation.</summary>
/// <param name="Factor">What earned the points.</param>
/// <param name="Detail">The applicant's figure for it.</param>
/// <param name="Points">The points earned.</param>
public sealed record ScoreLine(string Factor, string Detail, decimal Points);

/// <summary>An applicant's priority score with how it was derived.</summary>
/// <param name="Total">The score.</param>
/// <param name="Lines">The explanation, one line per factor.</param>
public sealed record PriorityScore(decimal Total, IReadOnlyList<ScoreLine> Lines);

/// <summary>
/// Waiting-list qualification and priority scoring (brief §4.10.3): the configurable rules an applicant must meet, and
/// a points score from years on the list, residency, dependants, disability and housing conditions - with a full
/// explanation an officer can print for the applicant. Pure and unit-tested.
/// </summary>
public static class WaitingListRules
{
    /// <summary>Full years between two dates.</summary>
    /// <param name="from">The earlier date.</param>
    /// <param name="to">The later date.</param>
    /// <returns>The completed years (0 when the dates are backwards).</returns>
    public static int FullYears(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            return 0;
        }

        int years = to.Year - from.Year;
        return from.AddYears(years) > to ? years - 1 : years;
    }

    /// <summary>Why an applicant does not qualify; empty when they do.</summary>
    /// <param name="facts">The applicant.</param>
    /// <param name="terms">The rules.</param>
    /// <param name="ownsProperty">Whether the applicant already owns or holds property in the area.</param>
    /// <param name="previouslyAllocated">Whether the applicant has already been allocated a stand or unit.</param>
    /// <param name="asAtDate">The date of the check.</param>
    /// <returns>The reasons.</returns>
    public static IReadOnlyList<string> Problems(ApplicantFacts facts, WaitingListTerms terms, bool ownsProperty, bool previouslyAllocated, DateOnly asAtDate)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(terms);
        List<string> problems = [];
        int age = FullYears(facts.DateOfBirth, asAtDate);
        if (age < terms.MinimumAge)
        {
            problems.Add($"Under the minimum age of {terms.MinimumAge} (aged {age}).");
        }

        int resident = FullYears(facts.ResidentSince, asAtDate);
        if (resident < terms.MinimumResidencyYears)
        {
            problems.Add($"Resident {resident} years; {terms.MinimumResidencyYears} required.");
        }

        if (terms.MaximumMonthlyIncome is decimal ceiling && facts.MonthlyIncome > ceiling)
        {
            problems.Add($"Monthly income {facts.MonthlyIncome:0.00} is above the {ceiling:0.00} limit.");
        }

        if (ownsProperty)
        {
            problems.Add("Already owns or holds property in the area.");
        }

        if (previouslyAllocated)
        {
            problems.Add("Already allocated a stand or unit.");
        }

        return problems;
    }

    /// <summary>The reasons a household fails the further qualification rules (none: it passes them).</summary>
    /// <param name="rules">The rules in force.</param>
    /// <param name="facts">The household.</param>
    /// <returns>The reasons.</returns>
    public static IReadOnlyList<string> Problems(QualificationRules rules, QualificationFacts facts)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(facts);
        List<string> problems = [];
        if (rules.CategoryIncomeCeilings.TryGetValue(facts.Category, out decimal ceiling) && facts.MonthlyIncome > ceiling)
        {
            problems.Add($"Monthly income {facts.MonthlyIncome:0.00} is above the {ceiling:0.00} limit for {facts.Category}.");
        }

        if (rules.FirstTimeBuyersOnly && facts.EverHeldProperty)
        {
            problems.Add("Not a first-time buyer: has held a stand or owned property in the area before.");
        }

        if (rules.RefuseForfeited && facts.Forfeited)
        {
            problems.Add("An earlier stand was repossessed or its agreement cancelled.");
        }

        if (rules.RequireRatesClearance && facts.OverdueCharges > 0m)
        {
            problems.Add($"Owes {facts.OverdueCharges:0.00} in overdue rates and charges; a clearance is required.");
        }

        return problems;
    }

    /// <summary>The priority score as at a date, with its explanation.</summary>
    /// <param name="facts">The applicant.</param>
    /// <param name="terms">The weights.</param>
    /// <param name="asAtDate">The date.</param>
    /// <returns>The score.</returns>
    public static PriorityScore Score(ApplicantFacts facts, WaitingListTerms terms, DateOnly asAtDate)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(terms);
        int onList = FullYears(facts.RegistrationDate, asAtDate);
        int resident = FullYears(facts.ResidentSince, asAtDate);
        decimal conditions = facts.Tenure switch
        {
            CurrentTenure.Homeless or CurrentTenure.InformalSettlement => terms.HomelessPoints,
            CurrentTenure.Lodger or CurrentTenure.WithRelatives => terms.LodgerPoints,
            _ => 0m,
        };

        List<ScoreLine> lines =
        [
            new("Years on the list", $"{onList} × {terms.PointsPerYearOnList:0.##}", onList * terms.PointsPerYearOnList),
            new("Years resident", $"{resident} × {terms.PointsPerYearResident:0.##}", resident * terms.PointsPerYearResident),
            new("Dependants", $"{facts.Dependants} × {terms.PointsPerDependant:0.##}", facts.Dependants * terms.PointsPerDependant),
            new("Disability", facts.HasDisability ? "Yes" : "No", facts.HasDisability ? terms.DisabilityPoints : 0m),
            new("Housing conditions", facts.Tenure.ToString(), conditions),
        ];

        // Age and cooperative membership count only where the council weights them.
        if (terms.PointsPerYearOfAge != 0m)
        {
            int over = Math.Max(0, FullYears(facts.DateOfBirth, asAtDate) - terms.AgePointsFromYears);
            lines.Add(new("Age", $"{over} year(s) over {terms.AgePointsFromYears} × {terms.PointsPerYearOfAge:0.##}", over * terms.PointsPerYearOfAge));
        }

        if (terms.CooperativeMemberPoints != 0m)
        {
            lines.Add(new("Cooperative member", facts.CooperativeMember ? "Yes" : "No", facts.CooperativeMember ? terms.CooperativeMemberPoints : 0m));
        }

        return new PriorityScore(lines.Sum(l => l.Points), lines);
    }
}
