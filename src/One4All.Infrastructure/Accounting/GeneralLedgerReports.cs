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

    /// <inheritdoc />
    public async Task<ChartOfAccountsReport> ChartOfAccountsAsync(long companyId, CancellationToken ct = default)
    {
        List<LedgerAccount> accounts = await _db.Accounts
            .Where(a => a.CompanyId == companyId)
            .ToListAsync(ct).ConfigureAwait(false);

        List<ChartOfAccountsRow> rows = accounts
            .OrderBy(a => a.AccountCode, StringComparer.Ordinal)
            .Select(a => new ChartOfAccountsRow(a.AccountCode, a.Name, a.Type.ToString(), a.ReportCategory, a.IsControlAccount, a.IsActive))
            .ToList();

        return new ChartOfAccountsReport(companyId, rows);
    }

    /// <inheritdoc />
    public async Task<FinancialSnapshotReport> FinancialSnapshotAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default)
    {
        BalanceSheet bs = await BalanceSheetAsync(companyId, asAtDate, ct).ConfigureAwait(false);
        IncomeStatement pl = await IncomeStatementAsync(companyId, new DateOnly(asAtDate.Year, 1, 1), asAtDate, ct).ConfigureAwait(false);

        List<FinancialSnapshotRow> rows =
        [
            new FinancialSnapshotRow("Total Assets", bs.TotalAssets),
            new FinancialSnapshotRow("Total Liabilities", bs.TotalLiabilities),
            new FinancialSnapshotRow("Total Equity", bs.TotalEquity),
            new FinancialSnapshotRow("Income (YTD)", pl.TotalIncome),
            new FinancialSnapshotRow("Expenses (YTD)", pl.TotalExpenses),
            new FinancialSnapshotRow("Net Profit (YTD)", pl.NetProfit),
        ];

        return new FinancialSnapshotReport(asAtDate, rows);
    }

    /// <inheritdoc />
    public async Task<VatReturnReport> VatReturnAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        // Resolve the VAT control accounts by their control-account type.
        List<string> outputCodes = await _db.Accounts
            .Where(a => a.CompanyId == companyId && a.ControlAccountType == ControlAccountType.VatOutput)
            .Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false);
        List<string> inputCodes = await _db.Accounts
            .Where(a => a.CompanyId == companyId && a.ControlAccountType == ControlAccountType.VatInput)
            .Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false);

        Dictionary<string, (decimal debit, decimal credit)> movement = await SumByAccountAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);

        decimal outputTax = outputCodes.Sum(c => movement.TryGetValue(c, out (decimal d, decimal cr) v) ? v.cr - v.d : 0m);
        decimal inputTax = inputCodes.Sum(c => movement.TryGetValue(c, out (decimal d, decimal cr) v) ? v.d - v.cr : 0m);

        return new VatReturnReport(fromDate, toDate, outputTax, inputTax, outputTax - inputTax);
    }

    /// <inheritdoc />
    public async Task<ControlReconciliationReport> ControlReconciliationAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default)
    {
        var controls = await _db.Accounts
            .Where(a => a.CompanyId == companyId && (a.ControlAccountType == ControlAccountType.AccountsReceivable
                || a.ControlAccountType == ControlAccountType.AccountsPayable || a.ControlAccountType == ControlAccountType.Stock))
            .Select(a => new { a.AccountCode, a.Name, a.ControlAccountType })
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<string, (decimal debit, decimal credit)> movement = await SumByAccountAsync(companyId, null, asAtDate, ct).ConfigureAwait(false);

        // Sub-ledger totals.
        List<Core.Trading.OpenItem> arItems = await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == Core.Trading.TradingLedger.AccountsReceivable && i.OutstandingBase != 0m && i.DocumentDate <= asAtDate)
            .ToListAsync(ct).ConfigureAwait(false);
        decimal arSub = arItems.Sum(i => i.Sign == Core.Trading.LedgerSign.Debit ? i.OutstandingBase : -i.OutstandingBase);

        List<Core.Trading.OpenItem> apItems = await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == Core.Trading.TradingLedger.AccountsPayable && i.OutstandingBase != 0m && i.DocumentDate <= asAtDate)
            .ToListAsync(ct).ConfigureAwait(false);
        decimal apSub = apItems.Sum(i => i.Sign == Core.Trading.LedgerSign.Credit ? i.OutstandingBase : -i.OutstandingBase);

        decimal stockSub = await _db.Set<Core.Inventory.StockBalance>()
            .Where(b => b.CompanyId == companyId)
            .SumAsync(b => (decimal?)b.TotalValue, ct).ConfigureAwait(false) ?? 0m;

        List<ControlReconciliationRow> rows = [];
        foreach (var c in controls.OrderBy(c => c.AccountCode, StringComparer.Ordinal))
        {
            (decimal d, decimal cr) = movement.GetValueOrDefault(c.AccountCode, (0m, 0m));
            (decimal glNatural, decimal sub, string label) = c.ControlAccountType switch
            {
                ControlAccountType.AccountsReceivable => (d - cr, arSub, "Accounts Receivable"),
                ControlAccountType.AccountsPayable => (cr - d, apSub, "Accounts Payable"),
                _ => (d - cr, stockSub, "Stock"),
            };
            rows.Add(new ControlReconciliationRow(label, c.AccountCode, glNatural, sub, glNatural - sub));
        }

        return new ControlReconciliationReport(asAtDate, rows);
    }

    /// <inheritdoc />
    public async Task<VatReturnByCodeReport> VatReturnByCodeAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        HashSet<string> outputCodes = (await _db.Accounts
            .Where(a => a.CompanyId == companyId && a.ControlAccountType == ControlAccountType.VatOutput)
            .Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        HashSet<string> inputCodes = (await _db.Accounts
            .Where(a => a.CompanyId == companyId && a.ControlAccountType == ControlAccountType.VatInput)
            .Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();

        List<string> vatCodes = outputCodes.Union(inputCodes).ToList();

        var lines = await (
            from l in _db.Set<JournalLine>()
            join j in _db.Journals on l.JournalEntryId equals j.Id
            where j.CompanyId == companyId && j.Status == JournalStatus.Posted && j.PostingDate >= fromDate && j.PostingDate <= toDate
                && vatCodes.Contains(l.AccountCode)
            select new { l.AccountCode, l.TaxCode, l.BaseDebit, l.BaseCredit })
            .ToListAsync(ct).ConfigureAwait(false);

        List<VatCodeRow> rows = lines
            .GroupBy(l => string.IsNullOrWhiteSpace(l.TaxCode) ? "(untagged)" : l.TaxCode!)
            .Select(g =>
            {
                decimal output = g.Where(l => outputCodes.Contains(l.AccountCode)).Sum(l => l.BaseCredit - l.BaseDebit);
                decimal input = g.Where(l => inputCodes.Contains(l.AccountCode)).Sum(l => l.BaseDebit - l.BaseCredit);
                return new VatCodeRow(g.Key, output, input, output - input);
            })
            .Where(r => r.OutputTax != 0m || r.InputTax != 0m)
            .OrderBy(r => r.TaxCode, StringComparer.Ordinal)
            .ToList();

        return new VatReturnByCodeReport(fromDate, toDate, rows, rows.Sum(r => r.Net));
    }

    private async Task<string> BaseCurrencyAsync(long companyId, CancellationToken ct)
    {
        Company? company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        return company?.BaseCurrency.Value ?? string.Empty;
    }
}
