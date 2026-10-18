using System.Globalization;

namespace One4All.Application.Reporting;

/// <summary>
/// The parameters every sales, purchasing, cash book, tax, general ledger, receivables, sales order, fixed asset, budget and job costing listing takes
/// besides its own (brief §9 "common parameters"): a reporting
/// currency with the rate basis to translate at, and a branch, cost centre and department to narrow postings to, and whether unposted
/// journals count. Listings declare them (see the reporting catalogue); the report runner translates amounts and names the rate basis
/// used in the header, and the providers that read journal lines apply the dimension filters.
/// </summary>
/// <param name="ReportingCurrency">The currency amounts are shown in; null or blank is the company's base currency.</param>
/// <param name="RateBasis">How the rate is chosen: "Closing" (closing rate at the report date), "Average" (period average), "Keyed" (the <paramref name="Rate"/> given), else the daily rate.</param>
/// <param name="Rate">A keyed rate (units of reporting currency per unit of base), used when given.</param>
/// <param name="Branch">The branch postings are narrowed to, when any.</param>
/// <param name="CostCentre">The cost centre postings are narrowed to, when any.</param>
/// <param name="Department">The department postings are narrowed to, when any.</param>
/// <param name="IncludeUnposted">Whether draft and deferred journals count besides posted ones.</param>
public sealed record ReportCommonParameters(string? ReportingCurrency, string? RateBasis, decimal? Rate, string? Branch, string? CostCentre, string? Department, bool IncludeUnposted)
{
    /// <summary>The parameter names, as listings declare them.</summary>
    public static readonly string[] Names = ["ReportingCurrency", "RateBasis", "Rate", "Branch", "CostCentre", "Department", "IncludeUnposted"];

    /// <summary>The parameter the runner adds to say which rate it used, shown in the listing header.</summary>
    public const string RateBasisUsed = "RateBasisUsed";

    /// <summary>The modules whose listings take the common parameters.</summary>
    public static readonly string[] Modules = ["Sales", "Purchasing", "CashBook", "Tax", "GeneralLedger", "Receivables", "SalesOrders", "FixedAssets", "Budget", "JobCosting"];

    /// <summary>
    /// The data commands whose providers narrow their postings by branch, cost centre and department and can count unposted journals (cash book, tax, ledger);
    /// other listings take the reporting currency only, so a header never claims a filter the rows did not get.
    /// </summary>
    public static readonly string[] DimensionCommandPrefixes = ["Cash", "Fatx", "Gled"];

    /// <summary>Whether a listing's data command applies the dimension filters.</summary>
    /// <param name="command">The data command.</param>
    /// <returns>True when its provider narrows by branch, cost centre and department.</returns>
    public static bool AppliesDimensions(string? command)
        => command is not null && DimensionCommandPrefixes.Any(p => command.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Nothing asked for: base currency, every dimension, posted journals only.</summary>
    public static ReportCommonParameters None { get; } = new(null, null, null, null, null, null, false);

    /// <summary>Whether amounts are to be translated into another currency.</summary>
    public bool WantsCurrency => !string.IsNullOrWhiteSpace(ReportingCurrency);

    /// <summary>Whether any dimension narrows the postings.</summary>
    public bool HasDimensionFilter => Branch is not null || CostCentre is not null || Department is not null;

    /// <summary>Reads the common parameters out of a report's parameter values.</summary>
    /// <param name="parameters">The values.</param>
    /// <returns>The common parameters (defaults where absent).</returns>
    public static ReportCommonParameters From(IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null)
        {
            return None;
        }

        return new ReportCommonParameters(Code(parameters, "ReportingCurrency"), Code(parameters, "RateBasis"), Number(parameters, "Rate"), Code(parameters, "Branch"), Code(parameters, "CostCentre"),
            Code(parameters, "Department"), Flag(parameters, "IncludeUnposted"));
    }

    private static string? Code(IReadOnlyDictionary<string, object?> parameters, string name)
        => parameters.TryGetValue(name, out object? value) && value is not null && Convert.ToString(value, CultureInfo.InvariantCulture) is string text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim().ToUpperInvariant() : null;

    private static decimal? Number(IReadOnlyDictionary<string, object?> parameters, string name)
        => parameters.TryGetValue(name, out object? value) && value is not null && value is not "" ? value switch
        {
            decimal d => d,
            double f => (decimal)f,
            int i => i,
            long l => l,
            string s when decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) => parsed,
            _ => null,
        } : null;

    private static bool Flag(IReadOnlyDictionary<string, object?> parameters, string name)
        => parameters.TryGetValue(name, out object? value) && value switch { bool b => b, string s => bool.TryParse(s, out bool parsed) && parsed, _ => false };
}
