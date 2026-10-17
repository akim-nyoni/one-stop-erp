using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// Consolidates several companies' ledgers into one trial balance by summing each account code across the
/// group from the posted journals. The plain consolidation requires a shared base currency; the translated one
/// translates each company into a presentation currency first (IAS 21 current-rate method) and carries the
/// difference as a translation reserve. Either way the result balances because each company's own postings do.
/// </summary>
public sealed class ConsolidationService : IConsolidationService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public ConsolidationService(One4AllDbContext db) : this(db, null)
    {
    }

    /// <summary>Creates the service under the installed licence.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="licence">The licence (consolidation needs its CONSOLIDATION feature).</param>
    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public ConsolidationService(One4AllDbContext db, Application.Abstractions.Licensing.ILicenceService? licence)
    {
        _db = db;
        _licence = licence;
    }

    private readonly Application.Abstractions.Licensing.ILicenceService? _licence;

    /// <inheritdoc />
    public Task<Result<ConsolidatedTrialBalance>> ConsolidatedTrialBalanceAsync(IReadOnlyList<long> companyIds, DateOnly asAtDate, CancellationToken ct = default) =>
        ConsolidatedTrialBalanceAsync(companyIds, asAtDate, [], ct);

    /// <inheritdoc />
    public async Task<Result<ConsolidatedTrialBalance>> ConsolidatedTrialBalanceAsync(IReadOnlyList<long> companyIds, DateOnly asAtDate, IReadOnlyCollection<string> intercompanyAccountCodes, CancellationToken ct = default)
    {
        if (Licensing.LicenceFeatureGate.Check(_licence, One4All.Licensing.Core.LicenceFeatures.Consolidation, "Consolidation") is { IsFailure: true } unlicensed)
        {
            return unlicensed.Error;
        }

        using IDisposable unscoped = _db.WithoutCompanyScope(); // a group reads every member company's books
        if (companyIds is null || companyIds.Count == 0)
        {
            return Error.Validation("Consolidation.Companies", "At least one company is required.");
        }

        long[] ids = companyIds.Distinct().ToArray();

        List<CurrencyCode> currencies = await _db.Companies
            .Where(c => ids.Contains(c.Id))
            .Select(c => c.BaseCurrency)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false);
        if (currencies.Count > 1)
        {
            return Error.Rule("Consolidation.MixedCurrency", "Consolidated companies must share a base currency.");
        }

        if (currencies.Count == 0)
        {
            return Error.NotFound("Consolidation.NoCompanies", "None of the given companies exist.");
        }

        var totals = await _db.Set<JournalLine>()
            .Where(l => _db.Journals.Any(j =>
                j.Id == l.JournalEntryId && ids.Contains(j.CompanyId) && j.Status == JournalStatus.Posted && j.PostingDate <= asAtDate))
            .GroupBy(l => l.AccountCode)
            .Select(g => new { Code = g.Key, Debit = g.Sum(x => x.BaseDebit), Credit = g.Sum(x => x.BaseCredit) })
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<string, string> names = await _db.Accounts
            .Where(a => ids.Contains(a.CompanyId))
            .GroupBy(a => a.AccountCode)
            .Select(g => new { Code = g.Key, Name = g.Min(a => a.Name)! })
            .ToDictionaryAsync(x => x.Code, x => x.Name, ct).ConfigureAwait(false);

        // Intercompany elimination: the due-from / due-to balances are eliminated to the extent they match (the lesser of the intercompany debits
        // and credits, spread pro rata); any residue stays on the accounts and is reported as unmatched for the group accountant to clear.
        HashSet<string> interco = new(intercompanyAccountCodes ?? [], StringComparer.Ordinal);
        Dictionary<string, decimal> nets = totals.ToDictionary(t => t.Code, t => t.Debit - t.Credit, StringComparer.Ordinal);
        decimal eliminated = 0m;
        decimal unmatched = 0m;
        if (interco.Count > 0)
        {
            List<KeyValuePair<string, decimal>> intercoNets = [.. nets.Where(n => interco.Contains(n.Key))];
            decimal debits = intercoNets.Where(n => n.Value > 0m).Sum(n => n.Value);
            decimal credits = -intercoNets.Where(n => n.Value < 0m).Sum(n => n.Value);
            unmatched = debits - credits;
            eliminated = Math.Min(debits, credits);
            if (eliminated > 0m)
            {
                foreach (KeyValuePair<string, decimal> n in intercoNets)
                {
                    nets[n.Key] = n.Value - (n.Value > 0m ? decimal.Round(eliminated * n.Value / debits, 2, MidpointRounding.ToEven)
                        : -decimal.Round(eliminated * -n.Value / credits, 2, MidpointRounding.ToEven));
                }
            }
        }

        List<ConsolidatedTrialBalanceRow> rows = [];
        foreach (var t in totals.OrderBy(t => t.Code, StringComparer.Ordinal))
        {
            decimal net = nets[t.Code];
            if (net == 0m)
            {
                continue;
            }

            rows.Add(new ConsolidatedTrialBalanceRow(
                t.Code,
                names.GetValueOrDefault(t.Code, t.Code),
                net > 0m ? net : 0m,
                net < 0m ? -net : 0m));
        }

        return new ConsolidatedTrialBalance(asAtDate, ids, rows, eliminated, unmatched);
    }

    /// <inheritdoc />
    public async Task<Result<TranslatedTrialBalance>> TranslatedTrialBalanceAsync(IReadOnlyList<long> companyIds, CurrencyCode presentationCurrency, DateOnly asAtDate, IReadOnlyCollection<string>? intercompanyAccountCodes = null, CancellationToken ct = default)
    {
        if (Licensing.LicenceFeatureGate.Check(_licence, One4All.Licensing.Core.LicenceFeatures.Consolidation, "Consolidation") is { IsFailure: true } unlicensed)
        {
            return unlicensed.Error;
        }

        using IDisposable unscoped = _db.WithoutCompanyScope(); // a group reads every member company's books
        if (companyIds is null || companyIds.Count == 0)
        {
            return Error.Validation("Consolidation.Companies", "At least one company is required.");
        }

        long[] ids = companyIds.Distinct().ToArray();
        var companies = await _db.Companies
            .Where(c => ids.Contains(c.Id))
            .OrderBy(c => c.Code)
            .Select(c => new { c.Id, c.Code, c.BaseCurrency })
            .ToListAsync(ct).ConfigureAwait(false);
        if (companies.Count == 0)
        {
            return Error.NotFound("Consolidation.NoCompanies", "None of the given companies exist.");
        }

        var accounts = await _db.Accounts
            .Where(a => ids.Contains(a.CompanyId))
            .Select(a => new { a.CompanyId, a.AccountCode, a.Name, a.Type })
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<(long, string), AccountType> types = accounts.ToDictionary(a => (a.CompanyId, a.AccountCode), a => a.Type);
        Dictionary<string, string> names = accounts
            .GroupBy(a => a.AccountCode, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Min(a => a.Name)!, StringComparer.Ordinal);

        // Movements by company, account and posting date: balance-sheet accounts use only the total, the others are
        // translated date by date.
        var movements = await (
            from l in _db.Set<JournalLine>()
            join j in _db.Journals on l.JournalEntryId equals j.Id
            where ids.Contains(j.CompanyId) && j.Status == JournalStatus.Posted && j.PostingDate <= asAtDate
            group l by new { j.CompanyId, l.AccountCode, j.PostingDate } into g
            select new { g.Key.CompanyId, g.Key.AccountCode, g.Key.PostingDate, Net = g.Sum(x => x.BaseDebit) - g.Sum(x => x.BaseCredit) })
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<string, decimal> translated = new(StringComparer.Ordinal);
        List<CompanyTranslation> translations = [];
        foreach (var company in companies)
        {
            bool same = company.BaseCurrency == presentationCurrency;
            decimal closing = 1m;
            if (!same)
            {
                Result<decimal> closingRate = await RateAsync(company.BaseCurrency, presentationCurrency, ExchangeRateType.ClosingRate, asAtDate, ct).ConfigureAwait(false);
                if (closingRate.IsFailure)
                {
                    return closingRate.Error;
                }

                closing = closingRate.Value;
            }

            Dictionary<DateOnly, decimal> dailyRates = [];
            decimal companyNet = 0m;
            foreach (var account in movements.Where(m => m.CompanyId == company.Id).GroupBy(m => m.AccountCode, StringComparer.Ordinal))
            {
                AccountType type = types.GetValueOrDefault((company.Id, account.Key), AccountType.Asset);
                decimal amount;
                if (same)
                {
                    amount = account.Sum(m => m.Net);
                }
                else if (type is AccountType.Asset or AccountType.Liability)
                {
                    amount = decimal.Round(account.Sum(m => m.Net) * closing, 2, MidpointRounding.ToEven);
                }
                else
                {
                    decimal sum = 0m;
                    foreach (var movement in account)
                    {
                        if (!dailyRates.TryGetValue(movement.PostingDate, out decimal rate))
                        {
                            Result<decimal> daily = await RateAsync(company.BaseCurrency, presentationCurrency, ExchangeRateType.Daily, movement.PostingDate, ct).ConfigureAwait(false);
                            if (daily.IsFailure)
                            {
                                return daily.Error;
                            }

                            dailyRates[movement.PostingDate] = rate = daily.Value;
                        }

                        sum += movement.Net * rate;
                    }

                    amount = decimal.Round(sum, 2, MidpointRounding.ToEven);
                }

                translated[account.Key] = translated.GetValueOrDefault(account.Key) + amount;
                companyNet += amount;
            }

            // The company balanced in its own currency; whatever it no longer balances by is its translation difference.
            translations.Add(new CompanyTranslation(company.Id, company.Code, company.BaseCurrency.Value, closing, companyNet));
        }

        HashSet<string> interco = new(intercompanyAccountCodes ?? [], StringComparer.Ordinal);
        decimal eliminated = 0m;
        decimal unmatched = 0m;
        if (interco.Count > 0)
        {
            // The matched portion goes (thirteenth audit #9: after translation the two sides rarely net to nil exactly, so all-or-nothing meant nothing was eliminated);
            // the residue stays on the intercompany accounts, reported as unmatched, for the group accountant to clear.
            List<KeyValuePair<string, decimal>> intercoTotals = [.. translated.Where(t => interco.Contains(t.Key))];
            decimal debits = intercoTotals.Where(t => t.Value > 0m).Sum(t => t.Value);
            decimal credits = -intercoTotals.Where(t => t.Value < 0m).Sum(t => t.Value);
            unmatched = debits - credits;
            eliminated = Math.Min(debits, credits);
            if (eliminated > 0m)
            {
                foreach (KeyValuePair<string, decimal> t in intercoTotals)
                {
                    decimal share = t.Value > 0m ? decimal.Round(eliminated * t.Value / debits, 2, MidpointRounding.ToEven) : -decimal.Round(eliminated * -t.Value / credits, 2, MidpointRounding.ToEven);
                    decimal left = t.Value - share;
                    if (left == 0m)
                    {
                        translated.Remove(t.Key);
                    }
                    else
                    {
                        translated[t.Key] = left;
                    }
                }
            }
        }

        List<ConsolidatedTrialBalanceRow> rows = [];
        foreach (KeyValuePair<string, decimal> t in translated.Where(t => t.Value != 0m).OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            rows.Add(new ConsolidatedTrialBalanceRow(t.Key, names.GetValueOrDefault(t.Key, t.Key), t.Value > 0m ? t.Value : 0m, t.Value < 0m ? -t.Value : 0m));
        }

        decimal reserve = translations.Sum(c => c.TranslationDifference);
        if (reserve != 0m)
        {
            rows.Add(new ConsolidatedTrialBalanceRow(TranslatedTrialBalance.TranslationReserveCode, "Foreign currency translation reserve",
                reserve < 0m ? -reserve : 0m, reserve > 0m ? reserve : 0m));
        }

        return new TranslatedTrialBalance(asAtDate, presentationCurrency.Value, rows, translations, reserve, eliminated, unmatched);
    }

    // The rate converting one unit of `from` into `to` in force on a date: the latest rate of the type quoted either
    // way round (an inverse quotation is inverted).
    private async Task<Result<decimal>> RateAsync(CurrencyCode from, CurrencyCode to, ExchangeRateType type, DateOnly date, CancellationToken ct)
    {
        decimal? rate = await ExchangeRateResolver.FindAsync(_db, from, to, type, date, ct).ConfigureAwait(false);
        return rate is { } value
            ? value
            : Error.Rule("Consolidation.NoRate", $"No {type} rate between {from} and {to} on or before {date:yyyy-MM-dd}.");
    }
}
