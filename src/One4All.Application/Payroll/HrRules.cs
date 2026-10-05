using One4All.Core.Payroll;

namespace One4All.Application.Payroll;

/// <summary>HR record rules: how long warnings stay live and how close a dated record is to lapsing.</summary>
public static class HrRules
{
    /// <summary>When a sanction lapses by default: verbal 6 months, written and final 12 months; suspension and dismissal do not lapse.</summary>
    /// <param name="sanction">The sanction.</param>
    /// <param name="date">The hearing date.</param>
    /// <returns>The lapse date, or null.</returns>
    public static DateOnly? WarningExpiry(DisciplinarySanction sanction, DateOnly date) => sanction switch
    {
        DisciplinarySanction.VerbalWarning => date.AddMonths(6),
        DisciplinarySanction.WrittenWarning or DisciplinarySanction.FinalWarning => date.AddMonths(12),
        _ => null,
    };

    /// <summary>Where a dated record stands at a date.</summary>
    /// <param name="expiresOn">When it lapses (null: never).</param>
    /// <param name="asAt">The date.</param>
    /// <param name="windowDays">How far ahead counts as expiring.</param>
    /// <returns>Expired, Expiring or Valid.</returns>
    public static string Status(DateOnly? expiresOn, DateOnly asAt, int windowDays)
        => expiresOn is not DateOnly e ? "Valid" : e < asAt ? "Expired" : e.DayNumber - asAt.DayNumber <= windowDays ? "Expiring" : "Valid";

    /// <summary>The required document types from a comma-separated list.</summary>
    /// <param name="list">The list (for example "ID, CONTRACT").</param>
    /// <returns>The normalised types.</returns>
    public static IReadOnlyList<string> RequiredTypes(string? list)
        => [.. (string.IsNullOrWhiteSpace(list) ? "ID,CONTRACT" : list).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(t => t.ToUpperInvariant()).Distinct()];
}
