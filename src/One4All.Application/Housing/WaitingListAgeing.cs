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
/// How long applicants have been waiting (brief §14 H20 Waiting List Age Analysis): &lt;1 year, 1–3, 3–5, 5–10, 10+ —
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
}
