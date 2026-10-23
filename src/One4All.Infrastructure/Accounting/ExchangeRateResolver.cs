using Microsoft.EntityFrameworkCore;
using One4All.Core.Accounting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// Finds the rate converting one currency into another (brief §3.3): rate tables hold quotes in whichever direction the
/// source publishes them, so a rate is found as the direct quote, else the inverse of the opposite quote, else
/// <b>triangulated</b> through a third currency both sides are quoted against (e.g. GBP→USD via GBP→EUR and EUR→USD).
/// Each leg uses its latest quote of the requested type effective on or before the date; direct and indirect quotations
/// are honoured.
/// </summary>
internal static class ExchangeRateResolver
{
    /// <summary>The units of <paramref name="to"/> one unit of <paramref name="from"/> buys, or null when no route exists.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="from">The currency converted from.</param>
    /// <param name="to">The currency converted to.</param>
    /// <param name="type">The rate type (daily, closing, average …).</param>
    /// <param name="date">The date the rate must be in force on.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rate, or null.</returns>
    /// <summary>
    /// The rate by the preferred type, else by any other type captured (daily, spot, closing, then period average): a counter taking a
    /// currency should not turn it away because the rate was captured under another heading.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="from">The currency converted from.</param>
    /// <param name="to">The currency converted to.</param>
    /// <param name="preferred">The rate type to look for first (null: daily).</param>
    /// <param name="date">The date the rate must be in force on.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rate, or null when no rate of any type is captured.</returns>
    public static async Task<decimal?> FindAnyAsync(One4AllDbContext db, CurrencyCode from, CurrencyCode to, ExchangeRateType? preferred, DateOnly date, CancellationToken ct)
    {
        ExchangeRateType first = preferred ?? ExchangeRateType.Daily;
        foreach (ExchangeRateType type in new[] { first, ExchangeRateType.Daily, ExchangeRateType.Spot, ExchangeRateType.ClosingRate, ExchangeRateType.PeriodAverage }.Distinct())
        {
            if (await FindAsync(db, from, to, type, date, ct).ConfigureAwait(false) is decimal rate && rate > 0m)
            {
                return rate;
            }
        }

        return null;
    }

    /// <summary>The rate type the company's exchange-rate policy names for a document type (else its source module), or null when there is no policy.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="documentType">The document type.</param>
    /// <param name="sourceModule">The source module.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rate type, or null.</returns>
    public static async Task<ExchangeRateType?> PolicyTypeAsync(One4AllDbContext db, long companyId, string? documentType, string? sourceModule, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        List<string> keys = [.. new[] { documentType, sourceModule }.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!)];
        if (keys.Count == 0)
        {
            return null;
        }

        List<ExchangeRatePolicy> policies = await db.ExchangeRatePolicies.AsNoTracking().Where(p => p.CompanyId == companyId && keys.Contains(p.DocumentType)).ToListAsync(ct).ConfigureAwait(false);
        return (policies.FirstOrDefault(p => p.DocumentType == documentType) ?? policies.FirstOrDefault(p => p.DocumentType == sourceModule))?.RateType;
    }

    /// <summary>The rate the company's policy prefers for a document (its rate type, else the daily rate), or null when none is captured.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="from">The currency converted from.</param>
    /// <param name="to">The currency converted to.</param>
    /// <param name="documentType">The document type.</param>
    /// <param name="sourceModule">The source module.</param>
    /// <param name="date">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rate, or null.</returns>
    public static async Task<decimal?> PolicyRateAsync(One4AllDbContext db, long companyId, CurrencyCode from, CurrencyCode to, string? documentType, string? sourceModule, DateOnly date, CancellationToken ct)
    {
        ExchangeRateType? preferred = await PolicyTypeAsync(db, companyId, documentType, sourceModule, ct).ConfigureAwait(false);
        decimal? rate = preferred is ExchangeRateType own && own != ExchangeRateType.Daily ? await FindAsync(db, from, to, own, date, ct).ConfigureAwait(false) : null;
        return rate ?? await FindAsync(db, from, to, ExchangeRateType.Daily, date, ct).ConfigureAwait(false);
    }

    public static async Task<decimal?> FindAsync(One4AllDbContext db, CurrencyCode from, CurrencyCode to, ExchangeRateType type, DateOnly date, CancellationToken ct)
    {
        if (from == to)
        {
            return 1m;
        }

        // Every quote of this type touching either currency, latest per ordered pair.
        List<ExchangeRate> quotes = await db.ExchangeRates.AsNoTracking()
            .Where(r => r.RateType == type && r.EffectiveFrom <= date
                && (r.FromCurrency == from || r.ToCurrency == from || r.FromCurrency == to || r.ToCurrency == to))
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<(CurrencyCode From, CurrencyCode To), decimal> latest = quotes
            .GroupBy(r => (r.FromCurrency, r.ToCurrency))
            .ToDictionary(g => g.Key, g => UnitsOfTo(g.OrderByDescending(r => r.EffectiveFrom).First()));

        decimal? Leg(CurrencyCode a, CurrencyCode b)
            => latest.TryGetValue((a, b), out decimal direct) ? direct
               : latest.TryGetValue((b, a), out decimal inverse) && inverse != 0m ? 1m / inverse
               : null;

        if (Leg(from, to) is { } rate)
        {
            return rate;
        }

        // Triangulate through a currency quoted against both sides (alphabetical, so the choice is stable).
        IEnumerable<CurrencyCode> withFrom = latest.Keys.Where(k => k.From == from || k.To == from).Select(k => k.From == from ? k.To : k.From);
        HashSet<CurrencyCode> withTo = [.. latest.Keys.Where(k => k.From == to || k.To == to).Select(k => k.From == to ? k.To : k.From)];
        foreach (CurrencyCode pivot in withFrom.Distinct().Where(withTo.Contains).OrderBy(c => c.Value, StringComparer.Ordinal))
        {
            if (Leg(from, pivot) is { } first && Leg(pivot, to) is { } second)
            {
                return first * second;
            }
        }

        return null;
    }

    private static decimal UnitsOfTo(ExchangeRate quote) => quote.Quotation == QuotationType.Direct ? quote.Rate : 1m / quote.Rate;
}
