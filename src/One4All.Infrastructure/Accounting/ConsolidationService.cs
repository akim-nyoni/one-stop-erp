using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// Consolidates several companies' ledgers into one trial balance by summing each account code across the
/// group from the posted journals. Requires the companies to share a base currency (no FX translation) and
/// produces a balanced result because each company's own postings balance.
/// </summary>
public sealed class ConsolidationService : IConsolidationService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public ConsolidationService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<ConsolidatedTrialBalance>> ConsolidatedTrialBalanceAsync(IReadOnlyList<long> companyIds, DateOnly asAtDate, CancellationToken ct = default)
    {
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

        List<ConsolidatedTrialBalanceRow> rows = [];
        foreach (var t in totals.OrderBy(t => t.Code, StringComparer.Ordinal))
        {
            decimal net = t.Debit - t.Credit;
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

        return new ConsolidatedTrialBalance(asAtDate, ids, rows);
    }
}
