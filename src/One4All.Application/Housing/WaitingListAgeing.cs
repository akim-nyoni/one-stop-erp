namespace One4All.Application.Housing;

/// <summary>A waiting-list age band (brief §14 H20).</summary>
public enum WaitingAgeBand
{
    /// <summary>Under a year.</summary>
    UnderOne = 0,

    /// <summary>One to three years.</summary>
    OneToThree = 1,

    /// <summary>Three to five years.</summary>
    ThreeToFive = 2,

    /// <summary>Five to ten years.</summary>
    FiveToTen = 3,

    /// <summary>Ten years and more.</summary>
    TenPlus = 4,
}

/// <summary>
/// How long applicants have been waiting (brief §14 H20 Waiting List Age Analysis): &lt;1 year, 1–3, 3–5, 5–10, 10+ -
/// in completed years from the registration date, each band including its lower bound. Pure and unit-tested.
/// </summary>
public static class WaitingListAgeing
{
    /// <summary>The band an applicant registered on a date falls in, as at another.</summary>
    /// <param name="registered">The registration date.</param>
    /// <param name="asAt">The date.</param>
    /// <returns>The band.</returns>
    public static WaitingAgeBand Band(DateOnly registered, DateOnly asAt) => WaitingListRules.FullYears(registered, asAt) switch
    {
        < 1 => WaitingAgeBand.UnderOne,
        < 3 => WaitingAgeBand.OneToThree,
        < 5 => WaitingAgeBand.ThreeToFive,
        < 10 => WaitingAgeBand.FiveToTen,
        _ => WaitingAgeBand.TenPlus,
    };

    /// <summary>An applicant's monthly income band (brief §14 H19).</summary>
    /// <param name="monthlyIncome">The monthly income.</param>
    /// <returns>The band's label.</returns>
    public static string IncomeBand(decimal monthlyIncome) => monthlyIncome switch
    {
        < 300m => "Under 300",
        < 600m => "300 – 599",
        < 1000m => "600 – 999",
        _ => "1,000 and over",
    };

    /// <summary>An applicant's age band (brief §14 H19), in completed years on a date.</summary>
    /// <param name="dateOfBirth">The date of birth, if recorded.</param>
    /// <param name="asAt">The date.</param>
    /// <returns>The band's label.</returns>
    public static string AgeBand(DateOnly? dateOfBirth, DateOnly asAt) => dateOfBirth is not DateOnly born
        ? "Not recorded"
        : WaitingListRules.FullYears(born, asAt) switch
        {
            < 26 => "Under 26",
            < 36 => "26 – 35",
            < 51 => "36 – 50",
            _ => "51 and over",
        };
}
