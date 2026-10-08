using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Housing;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Trading;

namespace One4All.Infrastructure.Reporting;

/// <summary>Serves the CB-numbered cash book reports (brief §14 D): every data command whose name starts with <c>Cash</c>.</summary>
public sealed class CashBookReportDataProvider : IReportDataProvider
{
    private readonly One4AllDbContext _db;
    private readonly Application.Abstractions.Banking.IBankReconciliationService? _reconciliation;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="reconciliation">The bank reconciliation service (statement and balances).</param>
    public CashBookReportDataProvider(One4AllDbContext db, Application.Abstractions.Banking.IBankReconciliationService? reconciliation = null)
    {
        _db = db;
        _reconciliation = reconciliation;
    }

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("Cash", StringComparison.OrdinalIgnoreCase)
        && command.ToUpperInvariant() is "CASHBOOKLISTING" or "CASHUNRECONCILED" or "CASHFORECAST" or "CASHPETTY" or "CASHTRANSFERS" or "CASHCHEQUES" or "CASHWALLETS" or "CASHRECSTATEMENT" or "CASHBALANCES";

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly asAt = DateParameter(parameters, "AsAtDate") ?? today;
        DateOnly toDate = DateParameter(parameters, "ToDate") ?? today;
        DateOnly fromDate = DateParameter(parameters, "FromDate") ?? new DateOnly(toDate.Year, 1, 1);
        return command.ToUpperInvariant() switch
        {
            "CASHCHEQUES" => await ChequesAsync(companyId, fromDate, toDate, parameters.TryGetValue("StaleMonths", out object? stale) && stale is not null and not "" ? Convert.ToInt32(stale, CultureInfo.InvariantCulture) : 6, ct)
                .ConfigureAwait(false),
            "CASHWALLETS" => await WalletsAsync(companyId, DateParameter(parameters, "AsAtDate") ?? toDate, ct).ConfigureAwait(false),
            "CASHBOOKLISTING" => await ListingAsync(companyId, Text(parameters, "BankCode")?.ToUpperInvariant(), fromDate, toDate, ct).ConfigureAwait(false),
            "CASHRECSTATEMENT" => await RecStatementAsync(companyId, Text(parameters, "BankCode")?.ToUpperInvariant(), asAt, ct).ConfigureAwait(false),
            "CASHBALANCES" => await BalancesAsync(companyId, asAt, ct).ConfigureAwait(false),
            "CASHUNRECONCILED" => await UnreconciledAsync(companyId, asAt, ct).ConfigureAwait(false),
            "CASHFORECAST" => await ForecastAsync(companyId, asAt, ct).ConfigureAwait(false),
            "CASHPETTY" => await PettyCashAsync(companyId, Text(parameters, "AccountCode"), fromDate, toDate, ct).ConfigureAwait(false),
            "CASHTRANSFERS" => await TransfersAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    // Posted lines on a set of ledger accounts, with their journal, in date order.
    private async Task<List<(JournalEntry Journal, JournalLine Line)>> LinesAsync(long companyId, IReadOnlyCollection<string> accounts, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
        => [.. (await (from l in _db.Set<JournalLine>().AsNoTracking() join j in _db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                      where j.CompanyId == companyId && (j.Status == JournalStatus.Posted || j.Status == JournalStatus.Reversed) && accounts.Contains(l.AccountCode)
                          && j.PostingDate >= fromDate && j.PostingDate <= toDate
                      orderby j.PostingDate, j.JournalNumber, l.Id
                      select new { Journal = j, Line = l }).ToListAsync(ct).ConfigureAwait(false)).Select(x => (x.Journal, x.Line))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ListingAsync(long companyId, string? bankCode, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<BankAccount> banks = await _db.BankAccounts.AsNoTracking().Where(b => b.CompanyId == companyId && (bankCode == null || b.Code == bankCode)).OrderBy(b => b.Code).ToListAsync(ct).ConfigureAwait(false);
        HashSet<long> matched = [.. await _db.BankStatementLines.AsNoTracking().Where(s => s.CompanyId == companyId && s.MatchedJournalLineId != null).Select(s => s.MatchedJournalLineId!.Value).ToListAsync(ct).ConfigureAwait(false)];
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (BankAccount bank in banks)
        {
            decimal balance = (await LinesAsync(companyId, [bank.GlAccountCode], DateOnly.MinValue, fromDate.AddDays(-1), ct).ConfigureAwait(false)).Sum(x => x.Line.TransactionDebit - x.Line.TransactionCredit);
            rows.Add(Row(("Bank", bank.Code), ("Date", string.Empty), ("Journal", string.Empty), ("Document", string.Empty), ("Narrative", "Opening balance"), ("Receipts", string.Empty), ("Payments", string.Empty),
                ("Balance", Money(balance)), ("Reconciled", string.Empty)));
            foreach ((JournalEntry journal, JournalLine line) in await LinesAsync(companyId, [bank.GlAccountCode], fromDate, toDate, ct).ConfigureAwait(false))
            {
                balance += line.TransactionDebit - line.TransactionCredit;
                rows.Add(Row(("Bank", bank.Code), ("Date", Date(journal.PostingDate)), ("Journal", journal.JournalNumber), ("Document", $"{journal.DocumentType} {journal.DocumentNumber}".Trim()),
                    ("Narrative", line.Narrative ?? journal.Narrative ?? string.Empty), ("Receipts", line.TransactionDebit == 0m ? string.Empty : Money(line.TransactionDebit)),
                    ("Payments", line.TransactionCredit == 0m ? string.Empty : Money(line.TransactionCredit)), ("Balance", Money(balance)), ("Reconciled", matched.Contains(line.Id) ? "Yes" : string.Empty)));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> UnreconciledAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<BankAccount> banks = await _db.BankAccounts.AsNoTracking().Where(b => b.CompanyId == companyId).OrderBy(b => b.Code).ToListAsync(ct).ConfigureAwait(false);
        HashSet<long> matched = [.. await _db.BankStatementLines.AsNoTracking().Where(s => s.CompanyId == companyId && s.MatchedJournalLineId != null).Select(s => s.MatchedJournalLineId!.Value).ToListAsync(ct).ConfigureAwait(false)];
        List<IReadOnlyDictionary<string, object?>> rows = [];
        IReadOnlyDictionary<string, object?> Aged(string bank, string kind, DateOnly date, string reference, decimal amount)
            => Row(("Bank", bank), ("Kind", kind), ("Date", Date(date)), ("Reference", reference), ("Days", Math.Max(0, asAt.DayNumber - date.DayNumber)),
                ("Band", DebtorAgeing.Bands[DebtorAgeing.Band(date, asAt)]), ("Amount", amount));
        foreach (BankAccount bank in banks)
        {
            foreach ((JournalEntry journal, JournalLine line) in (await LinesAsync(companyId, [bank.GlAccountCode], bank.ReconciledTo?.AddDays(1) ?? DateOnly.MinValue, asAt, ct).ConfigureAwait(false))
                         .Where(x => !matched.Contains(x.Line.Id)))
            {
                decimal amount = line.TransactionDebit - line.TransactionCredit;
                rows.Add(Aged(bank.Code, amount >= 0m ? "Deposit not yet cleared" : "Payment not yet presented", journal.PostingDate,
                    $"{journal.JournalNumber} {journal.DocumentNumber}".Trim(), amount));
            }

            foreach (BankStatementLine line in await _db.BankStatementLines.AsNoTracking().Where(s => s.BankAccountId == bank.Id && !s.IsReconciled && s.StatementDate <= asAt)
                         .OrderBy(s => s.StatementDate).ToListAsync(ct).ConfigureAwait(false))
            {
                rows.Add(Aged(bank.Code, "On statement, not in ledger", line.StatementDate, $"{line.Description} {line.Reference}".Trim(), line.Amount));
            }
        }

        return rows;
    }

    // Each currency starts from its bank balances at the date; receipts are customers' open items due in the week, payments suppliers'.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ForecastAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<BankAccount> banks = await _db.BankAccounts.AsNoTracking().Where(b => b.CompanyId == companyId && b.IsActive).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<string, decimal> opening = [];
        foreach (BankAccount bank in banks)
        {
            decimal balance = (await LinesAsync(companyId, [bank.GlAccountCode], DateOnly.MinValue, asAt, ct).ConfigureAwait(false)).Sum(x => x.Line.TransactionDebit - x.Line.TransactionCredit);
            opening[bank.Currency.Value] = opening.GetValueOrDefault(bank.Currency.Value) + balance;
        }

        List<OpenItemAsAt> receivable = await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, asAt, ct).ConfigureAwait(false);
        List<OpenItemAsAt> payable = await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsPayable, asAt, ct).ConfigureAwait(false);
        HashSet<string> currencies = [.. opening.Keys, .. receivable.Select(i => i.Item.Currency.Value), .. payable.Select(i => i.Item.Currency.Value)];
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (string currency in currencies.Order(StringComparer.Ordinal))
        {
            decimal cash = opening.GetValueOrDefault(currency);
            for (int week = 0; week < 13; week++)
            {
                DateOnly start = asAt.AddDays(week * 7), end = start.AddDays(6);
                bool Due(OpenItemAsAt i) => i.Item.Currency.Value == currency && (week == 0 ? i.Item.DueDate <= end : i.Item.DueDate >= start && i.Item.DueDate <= end);
                decimal receipts = receivable.Where(i => i.Item.Sign == LedgerSign.Debit && Due(i)).Sum(i => i.OutstandingTransaction);
                decimal payments = payable.Where(i => i.Item.Sign == LedgerSign.Credit && Due(i)).Sum(i => i.OutstandingTransaction);
                rows.Add(Row(("Week", Date(start)), ("Currency", currency), ("Opening", Money(cash)), ("Receipts", Money(receipts)), ("Payments", Money(payments)), ("Closing", Money(cash + receipts - payments))));
                cash += receipts - payments;
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PettyCashAsync(long companyId, string? accountCode, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<string> accounts = accountCode is not null ? [accountCode]
            : await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && a.Name.ToLower().Contains("petty")).Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false);
        if (accounts.Count == 0)
        {
            return [];
        }

        decimal balance = (await LinesAsync(companyId, accounts, DateOnly.MinValue, fromDate.AddDays(-1), ct).ConfigureAwait(false)).Sum(x => x.Line.BaseDebit - x.Line.BaseCredit);
        List<IReadOnlyDictionary<string, object?>> rows = [Row(("Date", string.Empty), ("Journal", string.Empty), ("Document", string.Empty), ("Narrative", "Opening balance"), ("In", string.Empty), ("Out", string.Empty), ("Balance", Money(balance)))];
        foreach ((JournalEntry journal, JournalLine line) in await LinesAsync(companyId, accounts, fromDate, toDate, ct).ConfigureAwait(false))
        {
            balance += line.BaseDebit - line.BaseCredit;
            rows.Add(Row(("Date", Date(journal.PostingDate)), ("Journal", journal.JournalNumber), ("Document", journal.DocumentNumber ?? "—"), ("Narrative", line.Narrative ?? journal.Narrative ?? string.Empty),
                ("In", line.BaseDebit == 0m ? string.Empty : Money(line.BaseDebit)), ("Out", line.BaseCredit == 0m ? string.Empty : Money(line.BaseCredit)), ("Balance", Money(balance))));
        }

        return rows;
    }

    // A transfer journal credits the paying bank and debits the receiving one; any other line is the exchange difference.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TransfersAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<string, string> banks = await _db.BankAccounts.AsNoTracking().Where(b => b.CompanyId == companyId).ToDictionaryAsync(b => b.GlAccountCode, b => b.Code, ct).ConfigureAwait(false);
        List<JournalEntry> journals = await _db.Journals.AsNoTracking().Include(j => j.Lines)
            .Where(j => j.CompanyId == companyId && (j.DocumentType == "BankTransfer" || j.DocumentType == "CB-XFER") && j.Status == JournalStatus.Posted && j.PostingDate >= fromDate && j.PostingDate <= toDate)
            .OrderBy(j => j.PostingDate).ThenBy(j => j.JournalNumber).ToListAsync(ct).ConfigureAwait(false);
        return [.. journals.Select(j =>
        {
            JournalLine? from = j.Lines.FirstOrDefault(l => banks.ContainsKey(l.AccountCode) && l.BaseCredit > 0m);
            JournalLine? to = j.Lines.FirstOrDefault(l => banks.ContainsKey(l.AccountCode) && l.BaseDebit > 0m);
            decimal difference = j.Lines.Where(l => !banks.ContainsKey(l.AccountCode)).Sum(l => l.BaseCredit - l.BaseDebit);
            return Row(("Date", Date(j.PostingDate)), ("Journal", j.JournalNumber), ("Document", j.DocumentNumber ?? "—"), ("From", from is null ? "?" : banks[from.AccountCode]), ("To", to is null ? "?" : banks[to.AccountCode]),
                ("Out", from is null ? "—" : $"{Money(from.TransactionCredit)} {from.TransactionCurrency.Value}"), ("In", to is null ? "—" : $"{Money(to.TransactionDebit)} {to.TransactionCurrency.Value}"),
                ("Difference", difference));
        })];
    }

    // A cheque still issued more than the stale limit after it was written is stale (as at the end of the period).
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ChequesAsync(long companyId, DateOnly fromDate, DateOnly toDate, int staleMonths, CancellationToken ct)
    {
        Dictionary<long, string> banks = await _db.BankAccounts.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(b => b.CompanyId == companyId).ToDictionaryAsync(b => b.Id, b => b.Code, ct).ConfigureAwait(false);
        List<Core.Banking.Cheque> cheques = await _db.Cheques.AsNoTracking().Where(c => c.CompanyId == companyId && c.IssuedOn >= fromDate && c.IssuedOn <= toDate).ToListAsync(ct).ConfigureAwait(false);
        return [.. cheques.OrderBy(c => banks.GetValueOrDefault(c.BankAccountId), StringComparer.Ordinal).ThenBy(c => c.ChequeNumber, StringComparer.Ordinal).Select(c =>
        {
            bool open = c.Status == Core.Banking.ChequeStatus.Issued;
            string status = open && c.IssuedOn.AddMonths(staleMonths) < toDate ? "Stale" : c.Status.ToString();
            return Row(("Bank", banks.GetValueOrDefault(c.BankAccountId, "?")), ("Cheque", c.ChequeNumber), ("Issued", Date(c.IssuedOn)), ("Payee", c.Payee), ("Document", c.DocumentNumber ?? "—"), ("Status", status),
                ("StatusDate", Date(c.StatusDate)), ("Days", open ? (toDate.DayNumber - c.IssuedOn.DayNumber).ToString(CultureInfo.InvariantCulture) : "—"), ("Reason", c.Reason ?? string.Empty), ("Amount", c.Amount));
        })];
    }

    // Ledger items not matched to a statement line and statement lines not yet reconciled should explain the whole gap.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> WalletsAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<Core.Banking.BankAccount> wallets = await _db.BankAccounts.AsNoTracking().Where(b => b.CompanyId == companyId && b.IsMobileWallet).OrderBy(b => b.Code).ToListAsync(ct).ConfigureAwait(false);
        HashSet<long> matched = [.. await _db.BankStatementLines.AsNoTracking().Where(s => s.CompanyId == companyId && s.MatchedJournalLineId != null).Select(s => s.MatchedJournalLineId!.Value).ToListAsync(ct).ConfigureAwait(false)];
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (Core.Banking.BankAccount wallet in wallets)
        {
            var lines = await (from l in _db.Set<JournalLine>().AsNoTracking()
                               join j in _db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                               where j.CompanyId == companyId && j.Status == JournalStatus.Posted && j.PostingDate <= asAt && l.AccountCode == wallet.GlAccountCode
                               select new { l.Id, Amount = l.TransactionDebit - l.TransactionCredit }).ToListAsync(ct).ConfigureAwait(false);
            List<Core.Banking.BankStatementLine> statement = await _db.BankStatementLines.AsNoTracking().Where(s => s.BankAccountId == wallet.Id && s.StatementDate <= asAt).ToListAsync(ct).ConfigureAwait(false);
            decimal ledger = lines.Sum(l => l.Amount), onStatement = statement.Sum(s => s.Amount);
            decimal ledgerOnly = lines.Where(l => !matched.Contains(l.Id)).Sum(l => l.Amount), statementOnly = statement.Where(s => !s.IsReconciled).Sum(s => s.Amount);
            rows.Add(Row(("Wallet", $"{wallet.Code} {wallet.Name}"), ("Ledger", Money(ledger)), ("Statement", Money(onStatement)), ("LedgerOnly", Money(ledgerOnly)), ("StatementOnly", Money(statementOnly)),
                ("Difference", ledger - onStatement - ledgerOnly + statementOnly)));
        }

        return rows;
    }

    // The classic statement: bank balance, less unpresented payments, plus uncleared receipts, equals the cash book (with any difference shown).
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RecStatementAsync(long companyId, string? bankCode, DateOnly asAt, CancellationToken ct)
    {
        if (_reconciliation is null)
        {
            return [];
        }

        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (BankAccount bank in await _db.BankAccounts.AsNoTracking().Where(b => b.CompanyId == companyId && b.IsActive && (bankCode == null || b.Code == bankCode)).OrderBy(b => b.Code)
                     .ToListAsync(ct).ConfigureAwait(false))
        {
            Result<Application.Abstractions.Banking.BankReconciliationReport> result = await _reconciliation.GetReconciliationAsync(companyId, bank.Id, asAt, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                continue;
            }

            Application.Abstractions.Banking.BankReconciliationReport rec = result.Value;
            string Head(string text) => $"{bank.Code} — {text}";
            rows.Add(Row(("Section", Head("Balance per bank statement")), ("Date", asAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("Description", bank.Name), ("Amount", rec.StatementBalance)));
            rows.AddRange(rec.UnpresentedGlItems.Where(i => i.Amount < 0m).Select(i => Row(("Section", Head("Less: payments not yet presented")), ("Date", i.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("Description", i.Description), ("Amount", i.Amount))));
            rows.AddRange(rec.UnpresentedGlItems.Where(i => i.Amount > 0m).Select(i => Row(("Section", Head("Add: receipts not yet on the statement")), ("Date", i.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("Description", i.Description), ("Amount", i.Amount))));
            rows.AddRange(rec.UnmatchedStatementItems.Select(i => Row(("Section", Head("Less: statement items not yet in the cash book")), ("Date", i.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("Description", i.Description), ("Amount", -i.Amount))));
            rows.Add(Row(("Section", Head("Balance per cash book")), ("Date", asAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("Description", bank.GlAccountCode), ("Amount", rec.GlBalance)));
            rows.Add(Row(("Section", Head(rec.Difference == 0m ? "Reconciled — no difference" : "UNEXPLAINED DIFFERENCE")), ("Date", string.Empty), ("Description", string.Empty), ("Amount", rec.Difference)));
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BalancesAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        if (_reconciliation is null || await _reconciliation.CashPositionAsync(companyId, asAt, ct).ConfigureAwait(false) is not { IsSuccess: true } position)
        {
            return [];
        }

        Dictionary<string, DateOnly?> reconciledTo = await _db.BankAccounts.AsNoTracking().Where(b => b.CompanyId == companyId).ToDictionaryAsync(b => b.Code, b => b.ReconciledTo, ct).ConfigureAwait(false);
        return [.. position.Value.Rows.Select(r => Row(("Code", r.Code), ("Name", r.Name), ("Currency", r.Currency), ("GlAccount", r.GlAccountCode), ("Unreconciled", r.UnreconciledLines),
            ("ReconciledTo", reconciledTo.GetValueOrDefault(r.Code)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "never"), ("Balance", r.Balance)))];
    }

    private static string? Text(IReadOnlyDictionary<string, object?> parameters, string name)
        => parameters.TryGetValue(name, out object? value) && value is string text && text.Trim().Length > 0 ? text.Trim() : null;

    private static DateOnly? DateParameter(IReadOnlyDictionary<string, object?> parameters, string name) => parameters.TryGetValue(name, out object? value) && value is DateOnly date ? date : null;

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells)
    {
        Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, object? value) in cells)
        {
            row[key] = value;
        }

        return row;
    }
}
