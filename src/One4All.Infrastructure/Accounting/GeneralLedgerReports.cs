using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// Produces the core GL financial reports from posted journals (brief §16 Phase 2). All figures are in
/// base currency and derived from <c>gl.journal_line</c> joined to posted <c>gl.journal</c> headers, so
/// the trial balance is guaranteed to balance whenever every journal balanced when posted.
/// </summary>
public sealed class GeneralLedgerReports : IGeneralLedgerReports
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the report service.</summary>
    /// <param name="db">The database context.</param>
    public GeneralLedgerReports(One4AllDbContext db) => _db = db;

    private sealed record AccountMeta(string Name, AccountType Type, string ReportCategory);

    private async Task<Dictionary<string, AccountMeta>> LoadAccountsAsync(long companyId, CancellationToken ct)
    {
        return await _db.Accounts
            .Where(a => a.CompanyId == companyId)
            .ToDictionaryAsync(a => a.AccountCode, a => new AccountMeta(a.Name, a.Type, a.ReportCategory), ct)
            .ConfigureAwait(false);
    }

    private async Task<Dictionary<string, (decimal debit, decimal credit)>> SumByAccountAsync(
        long companyId, DateOnly? from, DateOnly to, CancellationToken ct)
    {
        // Filter lines by their posted journal via a correlated subquery, then group by account code
        // in SQL. This translates cleanly (no projected-record grouping).
        var totals = await _db.Set<JournalLine>()
            .Where(l => _db.Journals.Any(j =>
                j.Id == l.JournalEntryId && j.CompanyId == companyId && j.Status == JournalStatus.Posted &&
                j.PostingDate <= to && (from == null || j.PostingDate >= from)))
            .GroupBy(l => l.AccountCode)
            .Select(g => new { Code = g.Key, Debit = g.Sum(x => x.BaseDebit), Credit = g.Sum(x => x.BaseCredit) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return totals.ToDictionary(t => t.Code, t => (t.Debit, t.Credit));
    }

    /// <inheritdoc />
    public async Task<TrialBalanceReport> TrialBalanceAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default)
    {
        Dictionary<string, AccountMeta> accounts = await LoadAccountsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<string, (decimal debit, decimal credit)> sums = await SumByAccountAsync(companyId, null, asAtDate, ct).ConfigureAwait(false);
        string currency = await BaseCurrencyAsync(companyId, ct).ConfigureAwait(false);

        List<TrialBalanceRow> rows = [];
        decimal totalDebit = 0m;
        decimal totalCredit = 0m;

        foreach ((string code, (decimal debit, decimal credit)) in sums.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            decimal net = debit - credit;
            if (net == 0m)
            {
                continue;
            }

            AccountMeta meta = accounts.GetValueOrDefault(code, new AccountMeta(code, AccountType.Asset, "Unknown"));
            decimal rowDebit = net > 0m ? net : 0m;
            decimal rowCredit = net < 0m ? -net : 0m;
            rows.Add(new TrialBalanceRow(code, meta.Name, meta.Type, rowDebit, rowCredit));
            totalDebit += rowDebit;
            totalCredit += rowCredit;
        }

        return new TrialBalanceReport(companyId, asAtDate, currency, rows, totalDebit, totalCredit);
    }

    /// <inheritdoc />
    public async Task<IncomeStatement> IncomeStatementAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        Dictionary<string, AccountMeta> accounts = await LoadAccountsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<string, (decimal debit, decimal credit)> sums = await SumByAccountAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);
        string currency = await BaseCurrencyAsync(companyId, ct).ConfigureAwait(false);

        List<StatementLine> income = [];
        List<StatementLine> expenses = [];

        foreach ((string code, (decimal debit, decimal credit)) in sums)
        {
            if (!accounts.TryGetValue(code, out AccountMeta? meta))
            {
                continue;
            }

            if (meta.Type == AccountType.Income)
            {
                decimal amount = credit - debit;
                if (amount != 0m)
                {
                    income.Add(new StatementLine(meta.ReportCategory, code, meta.Name, amount));
                }
            }
            else if (meta.Type == AccountType.Expense)
            {
                decimal amount = debit - credit;
                if (amount != 0m)
                {
                    expenses.Add(new StatementLine(meta.ReportCategory, code, meta.Name, amount));
                }
            }
        }

        return new IncomeStatement(companyId, fromDate, toDate, currency,
            income.OrderBy(l => l.AccountCode, StringComparer.Ordinal).ToList(),
            expenses.OrderBy(l => l.AccountCode, StringComparer.Ordinal).ToList());
    }

    /// <inheritdoc />
    public async Task<BalanceSheet> BalanceSheetAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default)
    {
        Dictionary<string, AccountMeta> accounts = await LoadAccountsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<string, (decimal debit, decimal credit)> sums = await SumByAccountAsync(companyId, null, asAtDate, ct).ConfigureAwait(false);
        string currency = await BaseCurrencyAsync(companyId, ct).ConfigureAwait(false);

        List<StatementLine> assets = [];
        List<StatementLine> liabilities = [];
        List<StatementLine> equity = [];
        decimal retainedEarnings = 0m;

        foreach ((string code, (decimal debit, decimal credit)) in sums)
        {
            if (!accounts.TryGetValue(code, out AccountMeta? meta))
            {
                continue;
            }

            switch (meta.Type)
            {
                case AccountType.Asset:
                    decimal a = debit - credit;
                    if (a != 0m) { assets.Add(new StatementLine(meta.ReportCategory, code, meta.Name, a)); }
                    break;
                case AccountType.Liability:
                    decimal l = credit - debit;
                    if (l != 0m) { liabilities.Add(new StatementLine(meta.ReportCategory, code, meta.Name, l)); }
                    break;
                case AccountType.Equity:
                    decimal e = credit - debit;
                    if (e != 0m) { equity.Add(new StatementLine(meta.ReportCategory, code, meta.Name, e)); }
                    break;
                case AccountType.Income:
                    retainedEarnings += credit - debit;
                    break;
                case AccountType.Expense:
                    retainedEarnings -= debit - credit;
                    break;
            }
        }

        // Current-year earnings (net profit to date) presented within equity so the sheet balances
        // before a formal year-end close rolls it to retained earnings.
        if (retainedEarnings != 0m)
        {
            equity.Add(new StatementLine("Retained Earnings", "*CYE", "Current-Year Earnings", retainedEarnings));
        }

        return new BalanceSheet(companyId, asAtDate, currency,
            assets.OrderBy(x => x.AccountCode, StringComparer.Ordinal).ToList(),
            liabilities.OrderBy(x => x.AccountCode, StringComparer.Ordinal).ToList(),
            equity.OrderBy(x => x.AccountCode, StringComparer.Ordinal).ToList());
    }

    /// <inheritdoc />
    public async Task<NominalActivityReport> NominalActivityAsync(long companyId, string accountCode, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        string code = accountCode.Trim().ToUpperInvariant();
        Dictionary<string, AccountMeta> accounts = await LoadAccountsAsync(companyId, ct).ConfigureAwait(false);
        string name = accounts.TryGetValue(code, out AccountMeta? meta) ? meta.Name : code;

        // Opening balance: everything strictly before the range start.
        var before = await (
            from l in _db.Set<JournalLine>()
            join j in _db.Journals on l.JournalEntryId equals j.Id
            where j.CompanyId == companyId && j.Status == JournalStatus.Posted && l.AccountCode == code && j.PostingDate < fromDate
            select new { l.BaseDebit, l.BaseCredit })
            .ToListAsync(ct).ConfigureAwait(false);
        decimal opening = before.Sum(l => l.BaseDebit) - before.Sum(l => l.BaseCredit);

        var inRange = await (
            from l in _db.Set<JournalLine>()
            join j in _db.Journals on l.JournalEntryId equals j.Id
            where j.CompanyId == companyId && j.Status == JournalStatus.Posted && l.AccountCode == code &&
                  j.PostingDate >= fromDate && j.PostingDate <= toDate
            orderby j.PostingDate, j.JournalNumber
            select new { j.PostingDate, j.JournalNumber, l.Narrative, l.BaseDebit, l.BaseCredit })
            .ToListAsync(ct).ConfigureAwait(false);

        List<NominalActivityLine> lines = [];
        decimal running = opening;
        foreach (var l in inRange)
        {
            running += l.BaseDebit - l.BaseCredit;
            lines.Add(new NominalActivityLine(l.PostingDate, l.JournalNumber, l.Narrative, l.BaseDebit, l.BaseCredit, running));
        }

        return new NominalActivityReport(companyId, code, name, opening, lines, running);
    }

    /// <inheritdoc />
    public async Task<JournalDaybookReport> JournalDaybookAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        // One row per posted journal in the range, with its base total (sum of debit lines = sum of credits).
        var journals = await (
            from j in _db.Journals
            where j.CompanyId == companyId && j.Status == JournalStatus.Posted &&
                  j.PostingDate >= fromDate && j.PostingDate <= toDate
            orderby j.PostingDate descending, j.JournalNumber descending
            select new
            {
                j.PostingDate,
                j.JournalNumber,
                j.SourceModule,
                j.DocumentType,
                j.DocumentNumber,
                j.Narrative,
                Amount = _db.Set<JournalLine>().Where(l => l.JournalEntryId == j.Id).Sum(l => (decimal?)l.BaseDebit) ?? 0m,
            })
            .ToListAsync(ct).ConfigureAwait(false);

        List<JournalDaybookLine> lines = journals
            .Select(j => new JournalDaybookLine(j.PostingDate, j.JournalNumber, j.SourceModule, j.DocumentType, j.DocumentNumber, j.Narrative, j.Amount))
            .ToList();

        return new JournalDaybookReport(companyId, fromDate, toDate, lines, lines.Sum(l => l.Amount));
    }

    /// <inheritdoc />
    public async Task<TrialBalanceMovementReport> TrialBalanceMovementAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        Dictionary<string, AccountMeta> accounts = await LoadAccountsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<string, (decimal debit, decimal credit)> opening = await SumByAccountAsync(companyId, null, fromDate.AddDays(-1), ct).ConfigureAwait(false);
        Dictionary<string, (decimal debit, decimal credit)> movement = await SumByAccountAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);

        List<TrialBalanceMovementRow> rows = [];
        foreach (string code in opening.Keys.Union(movement.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            (decimal od, decimal oc) = opening.GetValueOrDefault(code, (0m, 0m));
            (decimal md, decimal mc) = movement.GetValueOrDefault(code, (0m, 0m));
            decimal openingNet = od - oc;
            decimal closingNet = openingNet + (md - mc);

            if (openingNet == 0m && md == 0m && mc == 0m && closingNet == 0m)
            {
                continue;
            }

            string name = accounts.TryGetValue(code, out AccountMeta? meta) ? meta.Name : code;
            rows.Add(new TrialBalanceMovementRow(code, name, openingNet, md, mc, closingNet));
        }

        return new TrialBalanceMovementReport(companyId, fromDate, toDate, rows);
    }

    private async Task<string> BaseCurrencyAsync(long companyId, CancellationToken ct)
    {
        Company? company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        return company?.BaseCurrency.Value ?? string.Empty;
    }
}
