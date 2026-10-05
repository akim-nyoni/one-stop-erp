using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Trading;

namespace One4All.Infrastructure.Reporting;

/// <summary>Serves the GL-numbered ledger reports (brief §14 A): every data command whose name starts with <c>Gled</c>.</summary>
public sealed class LedgerReportDataProvider : IReportDataProvider
{
    private static readonly BudgetStatus[] InForce = [BudgetStatus.Approved, BudgetStatus.Active];

    private readonly One4AllDbContext _db;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    public LedgerReportDataProvider(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("Gled", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly asAt = DateParameter(parameters, "AsAtDate") ?? today;
        DateOnly toDate = DateParameter(parameters, "ToDate") ?? today;
        DateOnly fromDate = DateParameter(parameters, "FromDate") ?? new DateOnly(toDate.Year, 1, 1);
        return command.ToUpperInvariant() switch
        {
            "GLEDMULTICURRENCYTB" => await MultiCurrencyTbAsync(companyId, asAt, ct).ConfigureAwait(false),
            "GLEDJOURNALS" => await JournalsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "GLEDEQUITY" => await EquityAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "GLEDBUDGETCOMMITMENT" => await BudgetCommitmentAsync(companyId, asAt, ct).ConfigureAwait(false),
            "GLEDFXREVALUATION" => await DocumentLinesAsync(companyId, fromDate, toDate, d => d.StartsWith("FxRevaluation", StringComparison.Ordinal) || d.StartsWith("TradingRevaluation", StringComparison.Ordinal), true, ct).ConfigureAwait(false),
            "GLEDREALISEDFX" => await DocumentLinesAsync(companyId, fromDate, toDate, d => d.StartsWith("RealisedFx", StringComparison.Ordinal), false, ct).ConfigureAwait(false),
            "GLEDSUSPENSE" => await SuspenseAsync(companyId, asAt, ct).ConfigureAwait(false),
            "GLEDRECURRINGDUE" => await RecurringDueAsync(companyId, asAt, ct).ConfigureAwait(false),
            "GLEDACCRUALS" => await AccrualsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "GLEDINTERCOMPANY" => await IntercompanyAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private IQueryable<JournalLine> PostedLines(long companyId, DateOnly fromDate, DateOnly toDate)
        => from l in _db.Set<JournalLine>().AsNoTracking()
           join j in _db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
           where j.CompanyId == companyId && (j.Status == JournalStatus.Posted || j.Status == JournalStatus.Reversed) && j.PostingDate >= fromDate && j.PostingDate <= toDate
           select l;

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> MultiCurrencyTbAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<string, string> names = await AccountNamesAsync(companyId, ct).ConfigureAwait(false);
        var balances = await PostedLines(companyId, DateOnly.MinValue, asAt)
            .GroupBy(l => new { l.AccountCode, Currency = l.TransactionCurrency })
            .Select(g => new { g.Key.AccountCode, g.Key.Currency, Transaction = g.Sum(l => l.TransactionDebit - l.TransactionCredit), Base = g.Sum(l => l.BaseDebit - l.BaseCredit) })
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. balances.Where(b => b.Transaction != 0m || b.Base != 0m).OrderBy(b => b.AccountCode, StringComparer.Ordinal).ThenBy(b => b.Currency.Value, StringComparer.Ordinal)
            .Select(b => Row(("Account", b.AccountCode), ("Name", names.GetValueOrDefault(b.AccountCode, "?")), ("Currency", b.Currency.Value), ("Transaction", Money(b.Transaction)), ("Base", b.Base)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> JournalsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, string> users = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct).ConfigureAwait(false);
        List<JournalEntry> journals = await _db.Journals.AsNoTracking().Include(j => j.Lines).Where(j => j.CompanyId == companyId && j.PostingDate >= fromDate && j.PostingDate <= toDate)
            .OrderBy(j => j.PostingDate).ThenBy(j => j.JournalNumber).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> numbers = journals.ToDictionary(j => j.Id, j => j.JournalNumber);
        List<long> missing = [.. journals.Where(j => j.ReversalOfJournalId is long r && !numbers.ContainsKey(r)).Select(j => j.ReversalOfJournalId!.Value)];
        foreach (var other in await _db.Journals.AsNoTracking().Where(j => missing.Contains(j.Id)).Select(j => new { j.Id, j.JournalNumber }).ToListAsync(ct).ConfigureAwait(false))
        {
            numbers[other.Id] = other.JournalNumber;
        }

        string Who(long? id) => id is long u ? users.GetValueOrDefault(u, $"user {u}") : "—";
        return [.. journals.Select(j => Row(("Journal", j.JournalNumber), ("Date", Date(j.PostingDate)), ("Status", j.AwaitingApproval ? "Awaiting approval" : j.Status.ToString()), ("Source", j.SourceModule),
            ("Document", $"{j.DocumentType} {j.DocumentNumber}".Trim()), ("Manual", j.IsManual ? "Yes" : string.Empty), ("PreparedBy", Who(j.PreparedByUserId ?? j.CreatedByUserId)),
            ("ApprovedBy", Who(j.ApprovedByUserId)), ("Reversal", j.ReversalOfJournalId is long r ? $"of {numbers.GetValueOrDefault(r, "?")}" : string.Empty), ("Value", j.Lines.Sum(l => l.BaseDebit))))];
    }

    // Equity is credit-natural: balances and movements are shown credit-positive; the period's profit is income less expenses.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> EquityAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<LedgerAccount> accounts = await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        HashSet<string> equity = [.. accounts.Where(a => a.Type == AccountType.Equity).Select(a => a.AccountCode)];
        HashSet<string> income = [.. accounts.Where(a => a.Type == AccountType.Income).Select(a => a.AccountCode)];
        HashSet<string> expense = [.. accounts.Where(a => a.Type == AccountType.Expense).Select(a => a.AccountCode)];
        async Task<Dictionary<string, decimal>> CreditBalancesAsync(DateOnly start, DateOnly end)
            => await PostedLines(companyId, start, end).GroupBy(l => l.AccountCode).Select(g => new { g.Key, Net = g.Sum(l => l.BaseCredit - l.BaseDebit) })
                .ToDictionaryAsync(x => x.Key, x => x.Net, ct).ConfigureAwait(false);
        Dictionary<string, decimal> opening = await CreditBalancesAsync(DateOnly.MinValue, fromDate.AddDays(-1)).ConfigureAwait(false);
        Dictionary<string, decimal> movement = await CreditBalancesAsync(fromDate, toDate).ConfigureAwait(false);
        decimal profit = movement.Where(m => income.Contains(m.Key)).Sum(m => m.Value) + movement.Where(m => expense.Contains(m.Key)).Sum(m => m.Value);
        decimal priorProfit = opening.Where(m => income.Contains(m.Key) || expense.Contains(m.Key)).Sum(m => m.Value);
        List<IReadOnlyDictionary<string, object?>> rows = [.. accounts.Where(a => equity.Contains(a.AccountCode)).OrderBy(a => a.AccountCode, StringComparer.Ordinal)
            .Where(a => opening.GetValueOrDefault(a.AccountCode) != 0m || movement.GetValueOrDefault(a.AccountCode) != 0m)
            .Select(a => Row(("Line", $"{a.AccountCode} {a.Name}"), ("Opening", Money(opening.GetValueOrDefault(a.AccountCode))), ("Profit", Money(0m)),
                ("Movements", Money(movement.GetValueOrDefault(a.AccountCode))), ("Closing", opening.GetValueOrDefault(a.AccountCode) + movement.GetValueOrDefault(a.AccountCode))))];
        rows.Add(Row(("Line", "Retained profit not yet closed to equity"), ("Opening", Money(priorProfit)), ("Profit", Money(profit)), ("Movements", Money(0m)), ("Closing", priorProfit + profit)));
        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BudgetCommitmentAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        FiscalYear? year = await _db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(y => y.CompanyId == companyId && y.StartDate <= asAt && y.EndDate >= asAt, ct).ConfigureAwait(false);
        if (year is null)
        {
            return [];
        }

        int period = ((asAt.Year - year.StartDate.Year) * 12) + asAt.Month - year.StartDate.Month + 1;
        List<Budget> budgets = await _db.Budgets.AsNoTracking().Include(b => b.Lines)
            .Where(b => b.CompanyId == companyId && b.FiscalYearId == year.Id && b.IsActive && InForce.Contains(b.Status)).ToListAsync(ct).ConfigureAwait(false);
        var budgetLines = budgets.SelectMany(b => b.Lines.Where(l => l.Period <= period).Select(l => (Key: (l.AccountCode, Department: b.Department ?? string.Empty), l.Amount)))
            .GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        Dictionary<string, LedgerAccount> accounts = await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId).ToDictionaryAsync(a => a.AccountCode, ct).ConfigureAwait(false);
        var actualLines = await PostedLines(companyId, year.StartDate, asAt).GroupBy(l => new { l.AccountCode, l.Department })
            .Select(g => new { g.Key.AccountCode, g.Key.Department, Net = g.Sum(l => l.BaseDebit - l.BaseCredit) }).ToListAsync(ct).ConfigureAwait(false);
        var commitments = await _db.Set<BudgetCommitment>().AsNoTracking().Where(c => c.CompanyId == companyId && c.FiscalYearId == year.Id && c.Status == CommitmentStatus.Open)
            .Select(c => new { c.AccountCode, c.Department, c.Amount }).ToListAsync(ct).ConfigureAwait(false);
        return [.. budgetLines.OrderBy(b => b.Key.AccountCode, StringComparer.Ordinal).ThenBy(b => b.Key.Department, StringComparer.Ordinal).Select(b =>
        {
            LedgerAccount? account = accounts.GetValueOrDefault(b.Key.AccountCode);
            decimal sign = account?.Type is AccountType.Income or AccountType.Liability or AccountType.Equity ? -1m : 1m;
            decimal actual = sign * actualLines.Where(a => a.AccountCode == b.Key.AccountCode && (b.Key.Department.Length == 0 || a.Department == b.Key.Department)).Sum(a => a.Net);
            decimal committed = commitments.Where(c => c.AccountCode == b.Key.AccountCode && (b.Key.Department.Length == 0 || c.Department == b.Key.Department)).Sum(c => c.Amount);
            return Row(("Account", b.Key.AccountCode), ("Name", account?.Name ?? "?"), ("Department", b.Key.Department.Length == 0 ? "All" : b.Key.Department), ("Budget", Money(b.Value)),
                ("Actual", Money(actual)), ("Committed", Money(committed)),
                ("Used", b.Value == 0m ? "—" : ((actual + committed) * 100m / b.Value).ToString("0.0", CultureInfo.InvariantCulture) + "%"), ("Available", b.Value - actual - committed));
        })];
    }

    // Lines of the journals of a document type; for revaluations only the gain / loss side (the line not on the revalued balance) is the result.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DocumentLinesAsync(long companyId, DateOnly fromDate, DateOnly toDate, Func<string, bool> documentType, bool revaluation, CancellationToken ct)
    {
        List<LedgerAccount> accounts = await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        HashSet<string> pnl = [.. accounts.Where(a => a.Type is AccountType.Income or AccountType.Expense).Select(a => a.AccountCode)];
        List<JournalEntry> journals = [.. (await _db.Journals.AsNoTracking().Include(j => j.Lines)
                .Where(j => j.CompanyId == companyId && j.Status != JournalStatus.Cancelled && j.PostingDate >= fromDate && j.PostingDate <= toDate && j.DocumentType != null)
                .OrderBy(j => j.PostingDate).ThenBy(j => j.JournalNumber).ToListAsync(ct).ConfigureAwait(false))
            .Where(j => documentType(j.DocumentType!))];
        return [.. journals.SelectMany(j => j.Lines.Where(l => pnl.Contains(l.AccountCode)).Select(l => revaluation
            ? Row(("Date", Date(j.PostingDate)), ("Journal", j.JournalNumber), ("Type", j.DocumentType), ("Narrative", j.Narrative ?? string.Empty), ("Account", l.AccountCode), ("Amount", l.BaseCredit - l.BaseDebit))
            : Row(("Date", Date(j.PostingDate)), ("Journal", j.JournalNumber), ("Document", j.DocumentNumber ?? "—"), ("Narrative", j.Narrative ?? string.Empty), ("Account", l.AccountCode), ("Amount", l.BaseCredit - l.BaseDebit))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SuspenseAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<LedgerAccount> suspense = await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId
            && (a.Name.ToLower().Contains("suspense") || a.ReportCategory.ToLower().Contains("suspense"))).OrderBy(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false);
        List<string> codes = [.. suspense.Select(a => a.AccountCode)];
        var balances = await (from l in _db.Set<JournalLine>().AsNoTracking() join j in _db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                              where j.CompanyId == companyId && (j.Status == JournalStatus.Posted || j.Status == JournalStatus.Reversed) && j.PostingDate <= asAt && codes.Contains(l.AccountCode)
                              group new { l, j } by l.AccountCode into g
                              select new { g.Key, Net = g.Sum(x => x.l.BaseDebit - x.l.BaseCredit), Count = g.Count(), Oldest = g.Min(x => x.j.PostingDate) }).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [.. suspense.Select(a => (Account: a, Balance: balances.FirstOrDefault(b => b.Key == a.AccountCode))).Where(x => x.Balance is not null && x.Balance.Net != 0m)
            .Select(x => Row(("Item", $"Suspense: {x.Account.Name}"), ("Reference", x.Account.AccountCode), ("Count", x.Balance!.Count), ("Oldest", Date(x.Balance.Oldest)), ("Balance", x.Balance.Net)))];
        foreach ((TradingLedger ledger, LedgerSign unallocated, string label) in new[] { (TradingLedger.AccountsReceivable, LedgerSign.Credit, "Unallocated customer receipts & credits"), (TradingLedger.AccountsPayable, LedgerSign.Debit, "Unallocated supplier payments & debits") })
        {
            List<OpenItemAsAt> items = [.. (await OpenItemsAsAt.LoadAsync(_db, companyId, ledger, asAt, ct).ConfigureAwait(false)).Where(i => i.Item.Sign == unallocated && i.OutstandingBase != 0m)];
            if (items.Count > 0)
            {
                rows.Add(Row(("Item", label), ("Reference", ledger.ToString()), ("Count", items.Count), ("Oldest", Date(items.Min(i => i.Item.DocumentDate))), ("Balance", items.Sum(i => i.OutstandingBase))));
            }
        }

        return rows;
    }

    // LastPostedPeriod is yyyyMM; a template is due for the month before the date's month once that month has ended.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RecurringDueAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<RecurringJournal> templates = await _db.Set<RecurringJournal>().AsNoTracking().Include(r => r.Lines).Where(r => r.CompanyId == companyId && r.IsActive).OrderBy(r => r.Code).ToListAsync(ct).ConfigureAwait(false);
        int current = (asAt.Year * 12) + asAt.Month - 1;
        return [.. templates.Select(t =>
        {
            int? last = t.LastPostedPeriod is int p ? ((p / 100) * 12) + (p % 100) - 1 : null;
            int behind = last is int l ? current - l : int.MaxValue;
            string status = behind switch { <= 0 => "Posted for this month", 1 => "Due for this month", int.MaxValue => "Never posted", _ => $"Missed {behind - 1} month(s)" };
            return Row(("Code", t.Code), ("Description", t.Description), ("LastPosted", t.LastPostedPeriod is int lp ? $"{lp / 100}-{lp % 100:00}" : "—"), ("Status", status),
                ("Value", t.Lines.Where(x => x.IsDebit).Sum(x => x.Amount)));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AccrualsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        // Reversals are posted as REVERSAL journals pointing back at the accrual or prepayment they undo.
        Dictionary<long, JournalEntry> originals = await _db.Journals.AsNoTracking().Include(j => j.Lines)
            .Where(j => j.CompanyId == companyId && (j.DocumentType == "GL-ACCR" || j.DocumentType == "GL-PREP") && j.PostingDate <= toDate).ToDictionaryAsync(j => j.Id, ct).ConfigureAwait(false);
        List<long> ids = [.. originals.Keys];
        List<JournalEntry> reversals = await _db.Journals.AsNoTracking().Include(j => j.Lines)
            .Where(j => j.CompanyId == companyId && j.ReversalOfJournalId != null && ids.Contains(j.ReversalOfJournalId.Value) && j.PostingDate >= fromDate && j.PostingDate <= toDate)
            .ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [.. originals.Values.Where(j => j.PostingDate >= fromDate).Select(j => (Journal: j, Original: j, Reversal: false))
            .Concat(reversals.Select(r => (Journal: r, Original: originals[r.ReversalOfJournalId!.Value], Reversal: true)))
            .OrderBy(x => x.Journal.PostingDate).ThenBy(x => x.Journal.JournalNumber)
            .Select(x => Row(("Kind", (x.Original.DocumentType == "GL-ACCR" ? "Accrual" : "Prepayment") + (x.Reversal ? " reversal" : string.Empty)),
                ("Reference", x.Original.DocumentNumber ?? x.Original.JournalNumber), ("Date", Date(x.Journal.PostingDate)), ("Detail", x.Journal.Narrative ?? string.Empty), ("Recognised", string.Empty),
                ("Amount", (x.Reversal ? -1m : 1m) * x.Journal.Lines.Sum(l => l.BaseDebit))))];
        rows.AddRange((await _db.Set<DeferredIncomeSchedule>().AsNoTracking().Where(d => d.CompanyId == companyId && d.StartDate <= toDate).OrderBy(d => d.StartDate).ThenBy(d => d.Reference)
                .ToListAsync(ct).ConfigureAwait(false))
            .Select(d => Row(("Kind", "Deferred income"), ("Reference", d.Reference), ("Date", Date(d.StartDate)), ("Detail", $"{d.Description} to {Date(d.EndDate)}"),
                ("Recognised", Money(d.RecognisedAmount)), ("Amount", d.Amount - d.RecognisedAmount))));
        return rows;
    }

    // The mirror is the other company's Intercompany journal under the same document number.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> IntercompanyAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<JournalEntry> own = await _db.Journals.AsNoTracking().Include(j => j.Lines)
            .Where(j => j.CompanyId == companyId && j.DocumentType == "Intercompany" && j.Status == JournalStatus.Posted && j.PostingDate >= fromDate && j.PostingDate <= toDate)
            .OrderBy(j => j.PostingDate).ThenBy(j => j.DocumentNumber).ToListAsync(ct).ConfigureAwait(false);
        List<string?> documents = [.. own.Select(j => j.DocumentNumber)];
        var mirrors = await _db.Journals.AsNoTracking().IgnoreQueryFilters().Where(j => j.CompanyId != companyId && j.DocumentType == "Intercompany" && documents.Contains(j.DocumentNumber))
            .Select(j => new { j.DocumentNumber, j.CompanyId, j.Status }).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> companies = await _db.Companies.AsNoTracking().IgnoreQueryFilters().ToDictionaryAsync(c => c.Id, c => c.Code, ct).ConfigureAwait(false);
        return [.. own.Select(j =>
        {
            var mirror = mirrors.FirstOrDefault(m => m.DocumentNumber == j.DocumentNumber);
            return Row(("Document", j.DocumentNumber ?? "—"), ("Date", Date(j.PostingDate)), ("Narrative", j.Narrative ?? string.Empty),
                ("Counterparty", mirror is null ? "—" : companies.GetValueOrDefault(mirror.CompanyId, "?")), ("Status", mirror is null ? "Unmatched" : mirror.Status == JournalStatus.Posted ? "Matched" : $"Mirror {mirror.Status}"),
                ("Amount", j.Lines.Sum(l => l.BaseDebit)));
        })];
    }

    private Task<Dictionary<string, string>> AccountNamesAsync(long companyId, CancellationToken ct)
        => _db.Accounts.AsNoTracking().IgnoreQueryFilters().Where(a => a.CompanyId == companyId).ToDictionaryAsync(a => a.AccountCode, a => a.Name, ct);

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
