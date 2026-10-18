using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Reporting;
using One4All.Core.Accounting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using One4All.Reporting.Model;

namespace One4All.Infrastructure.Reporting;

/// <summary>
/// Translates a listing's money columns into the reporting currency asked for (brief §9 common parameters: "currency basis") and says
/// which rate it used - the closing rate at the report date, the period average, the daily rate, or a keyed rate - so the header can
/// show it. Money columns are the fields the listing formats as amounts; cells may be numbers or already-formatted text.
/// </summary>
internal static class ReportCurrencyTranslation
{
    /// <summary>Translates the rows when a reporting currency is asked for.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="definition">The report definition (its money fields).</param>
    /// <param name="rows">The rows as the provider gave them.</param>
    /// <param name="parameters">The report's parameter values.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rows (translated when asked) and the parameter values with the rate basis used added.</returns>
    public static async Task<(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, IReadOnlyDictionary<string, object?> Parameters)> ApplyAsync(One4AllDbContext db, long companyId,
        ReportDefinition definition, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        ReportCommonParameters common = ReportCommonParameters.From(parameters);
        if (!ReportCommonParameters.Modules.Contains(definition.Module, StringComparer.Ordinal))
        {
            return (rows, parameters);
        }

        Dictionary<string, object?> shown = new(parameters, StringComparer.OrdinalIgnoreCase);
        CurrencyCode baseCurrency = await db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        if (!common.WantsCurrency || !CurrencyCode.TryParse(common.ReportingCurrency, out CurrencyCode reporting) || reporting == baseCurrency)
        {
            shown[ReportCommonParameters.RateBasisUsed] = $"Amounts in {baseCurrency.Value} (base currency)" + Narrowed(common);
            return (rows, shown);
        }

        DateOnly date = Date(parameters, "AsAtDate") ?? Date(parameters, "ToDate") ?? DateOnly.FromDateTime(DateTime.Today);
        (decimal? rate, string basis) = common.RateBasis?.ToUpperInvariant() switch
        {
            "KEYED" when common.Rate is > 0m => (common.Rate, "keyed rate"),
            "CLOSING" => (await Accounting.ExchangeRateResolver.FindAsync(db, baseCurrency, reporting, ExchangeRateType.ClosingRate, date, ct).ConfigureAwait(false), $"closing rate at {date:yyyy-MM-dd}"),
            "AVERAGE" => (await Accounting.ExchangeRateResolver.FindAsync(db, baseCurrency, reporting, ExchangeRateType.PeriodAverage, date, ct).ConfigureAwait(false), $"period average rate to {date:yyyy-MM-dd}"),
            _ when common.Rate is > 0m => (common.Rate, "keyed rate"),
            _ => (await Accounting.ExchangeRateResolver.FindAsync(db, baseCurrency, reporting, ExchangeRateType.Daily, date, ct).ConfigureAwait(false), $"daily rate at {date:yyyy-MM-dd}"),
        };
        if (rate is not decimal factor || factor <= 0m)
        {
            shown[ReportCommonParameters.RateBasisUsed] = $"No {reporting.Value} rate on file for {date:yyyy-MM-dd}: amounts in {baseCurrency.Value} (base currency)" + Narrowed(common);
            return (rows, shown);
        }

        // Money cells: the fields the listing formats as amounts, and otherwise any decimal or two-decimal text in a column that is not a rate, share, count or quantity.
        HashSet<string> moneyFields = [.. definition.Bands.SelectMany(b => b.Elements).Where(e => e.Kind == ElementKind.Field && e.Format == "N2" && !string.IsNullOrEmpty(e.FieldName)).Select(e => e.FieldName!)];
        List<IReadOnlyDictionary<string, object?>> translated = new(rows.Count);
        foreach (IReadOnlyDictionary<string, object?> row in rows)
        {
            Dictionary<string, object?> copy = new(row, StringComparer.OrdinalIgnoreCase);
            foreach ((string field, object? value) in row)
            {
                if (moneyFields.Contains(field) || (LooksLikeMoney(value) && !NotMoney.IsMatch(field)))
                {
                    copy[field] = Translate(value, factor);
                }
            }

            translated.Add(copy);
        }

        shown[ReportCommonParameters.RateBasisUsed] = $"Amounts in {reporting.Value} at {factor.ToString("0.####", CultureInfo.InvariantCulture)} {reporting.Value}/{baseCurrency.Value} ({basis})" + Narrowed(common);
        return (translated, shown);
    }

    // Columns that are not base-currency money: rates, shares and counts, and amounts already in a currency of their own (transaction, foreign, reporting).
    private static readonly System.Text.RegularExpressions.Regex NotMoney = new("rate|percent|share|days|count|qty|quantity|items|documents|customers|suppliers|rank|number|weight|hours|units"
        + "|transaction|foreign|reporting|year|period|months|life|age|version|sequence|score|size|area", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex TwoDecimals = new(@"^-?[0-9][0-9,]*\.[0-9]{2}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool LooksLikeMoney(object? value) => value is decimal || (value is string s && TwoDecimals.IsMatch(s));

    private static string Narrowed(ReportCommonParameters common)
    {
        List<string> parts = [];
        if (common.Branch is not null) { parts.Add($"branch {common.Branch}"); }
        if (common.CostCentre is not null) { parts.Add($"cost centre {common.CostCentre}"); }
        if (common.Department is not null) { parts.Add($"department {common.Department}"); }
        if (common.IncludeUnposted) { parts.Add("unposted journals included"); }
        return parts.Count == 0 ? string.Empty : "; " + string.Join(", ", parts);
    }

    private static object? Translate(object? value, decimal factor) => value switch
    {
        decimal d => decimal.Round(d * factor, 2, MidpointRounding.AwayFromZero),
        double f => (double)decimal.Round((decimal)f * factor, 2, MidpointRounding.AwayFromZero),
        int i => decimal.Round(i * factor, 2, MidpointRounding.AwayFromZero),
        long l => decimal.Round(l * factor, 2, MidpointRounding.AwayFromZero),
        string s when s.Length > 0 && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
            => decimal.Round(parsed * factor, 2, MidpointRounding.AwayFromZero).ToString("N2", CultureInfo.InvariantCulture),
        _ => value,
    };

    private static DateOnly? Date(IReadOnlyDictionary<string, object?> parameters, string name)
        => parameters.TryGetValue(name, out object? value) ? value switch { DateOnly d => d, DateTime t => DateOnly.FromDateTime(t), string s when DateOnly.TryParse(s, CultureInfo.InvariantCulture, out DateOnly p) => p, _ => null } : null;
}
