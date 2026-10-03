using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>IAS 29 restatement over the posted ledger and the company's monthly price index.</summary>
public sealed class HyperinflationService : IHyperinflationService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public HyperinflationService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SetIndexAsync(long companyId, string indexCode, DateOnly month, decimal value, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(indexCode) || value <= 0m)
        {
            return Result.Failure(Error.Validation("Ias29.InvalidIndex", "A price index needs a code and a positive value."));
        }

        PriceIndex entry = new(companyId, indexCode, month, value);
        PriceIndex? existing = await _db.Set<PriceIndex>()
            .FirstOrDefaultAsync(i => i.CompanyId == companyId && i.IndexCode == entry.IndexCode && i.Month == entry.Month, ct).ConfigureAwait(false);
        if (existing is null)
        {
            _db.Set<PriceIndex>().Add(entry);
        }
        else
        {
            existing.Value = value;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<RestatedTrialBalance>> RestatedTrialBalanceAsync(long companyId, DateOnly asAtDate, string indexCode = "CPI", CancellationToken ct = default)
    {
        string code = indexCode.Trim().ToUpperInvariant();
        FiscalYear? year = await _db.FiscalYears.AsNoTracking()
            .FirstOrDefaultAsync(y => y.CompanyId == companyId && y.StartDate <= asAtDate && y.EndDate >= asAtDate, ct).ConfigureAwait(false);
        if (year is null)
        {
            return Error.NotFound("Ias29.NoFiscalYear", $"No financial year contains {asAtDate:yyyy-MM-dd}.");
        }

        Dictionary<string, LedgerAccount> accounts = await _db.Accounts.AsNoTracking().IgnoreQueryFilters()
            .Where(a => a.CompanyId == companyId).ToDictionaryAsync(a => a.AccountCode, ct).ConfigureAwait(false);

        var movements = await (
            from l in _db.Set<JournalLine>()
            join j in _db.Journals on l.JournalEntryId equals j.Id
            where j.CompanyId == companyId && (j.Status == JournalStatus.Posted || j.Status == JournalStatus.Reversed) && j.PostingDate <= asAtDate
            group l by new { l.AccountCode, j.PostingDate.Year, j.PostingDate.Month } into g
            select new { g.Key.AccountCode, g.Key.Year, g.Key.Month, Amount = g.Sum(x => x.BaseDebit - x.BaseCredit) })
            .ToListAsync(ct).ConfigureAwait(false);

        // Income and expenses restate only this year's amounts: earlier years' have been closed to equity.
        bool InScope(string account, DateOnly month) => accounts[account].Type is AccountType.Asset or AccountType.Liability or AccountType.Equity
            || month >= new DateOnly(year.StartDate.Year, year.StartDate.Month, 1);
        bool Monetary(LedgerAccount a) => Hyperinflation.IsMonetary(a.Type, a.ControlAccountType, a.Ias29);

        List<(LedgerAccount Account, List<MonthlyMovement> Movements)> perAccount = [.. movements
            .Select(m => (m.AccountCode, Movement: new MonthlyMovement(new DateOnly(m.Year, m.Month, 1), m.Amount)))
            .Where(m => InScope(m.AccountCode, m.Movement.Month))
            .GroupBy(m => m.AccountCode)
            .Select(g => (accounts[g.Key], g.Select(x => x.Movement).ToList()))
            .Where(a => a.Item2.Sum(m => m.Amount) != 0m || !Monetary(a.Item1))
            .OrderBy(a => a.Item1.AccountCode, StringComparer.Ordinal)];

        DateOnly closingMonth = new(asAtDate.Year, asAtDate.Month, 1);
        List<DateOnly> needed = [.. perAccount.Where(a => !Monetary(a.Account)).SelectMany(a => a.Movements.Select(m => m.Month)).Append(closingMonth).Distinct().Order()];
        Dictionary<DateOnly, decimal> index = await _db.Set<PriceIndex>().AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.IndexCode == code && needed.Contains(i.Month))
            .ToDictionaryAsync(i => i.Month, i => i.Value, ct).ConfigureAwait(false);
        List<DateOnly> missing = [.. needed.Where(m => !index.ContainsKey(m))];
        if (missing.Count > 0)
        {
            return Error.Rule("Ias29.MissingIndex", $"The {code} index is missing for {string.Join(", ", missing.Select(m => m.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)))}.");
        }

        decimal closing = index[closingMonth];
        List<RestatedTrialBalanceRow> rows = [];
        foreach ((LedgerAccount account, List<MonthlyMovement> monthly) in perAccount)
        {
            decimal historical = monthly.Sum(m => m.Amount);
            bool monetary = Monetary(account);
            decimal restated = monetary ? historical : Hyperinflation.Restate(monthly, m => index[m], closing);
            if (historical != 0m || restated != 0m)
            {
                rows.Add(new RestatedTrialBalanceRow(account.AccountCode, account.Name, account.Type, monetary, historical, restated));
            }
        }

        return new RestatedTrialBalance(asAtDate, code, closing, rows);
    }
}
