using System.Globalization;
using One4All.Licensing.Core;

namespace One4All.Licensing.Generator.Vendor;

/// <summary>
/// A licence or module term (brief §11.3: arbitrary timeframes): <c>perpetual</c>, a number of days, months or years (<c>30d</c>, <c>6m</c>,
/// <c>1y</c>), or a specific end date (<c>2027-06-30</c>).
/// </summary>
public static class LicenceTerm
{
    /// <summary>Works out the last valid day of a term starting on a day.</summary>
    /// <param name="spec">The term.</param>
    /// <param name="start">The first valid day.</param>
    /// <returns>The last valid day, or null for perpetual.</returns>
    /// <exception cref="FormatException">Thrown when the term is not understood or ends before it starts.</exception>
    public static DateOnly? EndOf(string spec, DateOnly start)
    {
        string text = (spec ?? string.Empty).Trim().ToLowerInvariant();
        if (text is "perpetual" or "none" or "")
        {
            return null;
        }

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            return date >= start ? date : throw new FormatException($"The end date {date:yyyy-MM-dd} is before the start {start:yyyy-MM-dd}.");
        }

        if (text.Length >= 2 && int.TryParse(text[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0)
        {
            return text[^1] switch
            {
                'd' => start.AddDays(n - 1),
                'm' => start.AddMonths(n).AddDays(-1),
                'y' => start.AddYears(n).AddDays(-1),
                _ => throw new FormatException($"Term '{spec}' is not understood (use perpetual, 30d, 6m, 1y or yyyy-MM-dd)."),
            };
        }

        throw new FormatException($"Term '{spec}' is not understood (use perpetual, 30d, 6m, 1y or yyyy-MM-dd).");
    }
}

/// <summary>The modules each edition ticks by default (brief §11.4 edition templates); the vendor adds or removes from there.</summary>
public static class EditionTemplates
{
    private static readonly string[] Financials = ["GL", "AR", "AP", "CB"];

    /// <summary>The default modules of an edition.</summary>
    /// <param name="edition">The edition.</param>
    /// <returns>The module codes.</returns>
    public static IReadOnlyList<string> ModulesFor(LicenceEdition edition) => edition switch
    {
        LicenceEdition.Standard => [.. Financials, "INV", "POS"],
        LicenceEdition.Professional => [.. Financials, "INV", "POP", "SOP", "POS", "VOU", "FA", "CRM", "REPORT_DESIGNER"],
        LicenceEdition.Enterprise => [.. Financials, "INV", "POP", "SOP", "POS", "VOU", "FA", "CRM", "JOB", "PAY", "BUD", "REPORT_DESIGNER"],
        LicenceEdition.Municipal => [.. Financials, "INV", "POP", "FA", "MUNI", "HSE", "BUD", "PAY", "REPORT_DESIGNER"],
        LicenceEdition.Education => [.. Financials, "INV", "POP", "FA", "SCH", "BUD", "PAY", "REPORT_DESIGNER"],
        _ => Financials,
    };

    /// <summary>A rank for telling an upgrade from a downgrade.</summary>
    /// <param name="edition">The edition.</param>
    /// <returns>Standard lowest; Enterprise, Municipal and Education alike.</returns>
    public static int Rank(LicenceEdition edition) => edition switch
    {
        LicenceEdition.Standard => 0,
        LicenceEdition.Professional => 1,
        _ => 2,
    };
}
