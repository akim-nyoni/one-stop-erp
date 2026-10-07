using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceProvider _services;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="services">Resolves the close checklist and IAS 29 services when those reports run.</param>
    public LedgerReportDataProvider(One4AllDbContext db, IServiceProvider services)
    {
        _db = db;
        _services = services;
    }

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
            "GLEDCLOSESTATUS" => await CloseStatusAsync(companyId, asAt, ct).ConfigureAwait(false),
            "GLEDMULTICURRENCYTB" => await MultiCurrencyTbAsync(companyId, asAt, ct).ConfigureAwait(false),
            "GLEDJOURNALS" => await JournalsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "GLEDEQUITY" => await EquityAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "GLEDBUDGETCOMMITMENT" => await BudgetCommitmentAsync(companyId, asAt, ct).ConfigureAwait(false),
            "GLEDFXREVALUATION" => await DocumentLinesAsync(companyId, fromDate, toDate, d => d.StartsWith("FxRevaluation", StringComparison.Ordinal) || d.StartsWith("TradingRevaluation", StringComparison.Ordinal), true, ct).ConfigureAwait(false),
            "GLEDREALISEDFX" => await DocumentLinesAsync(companyId, fromDate, toDate, d => d.StartsWith("RealisedFx", StringComparison.Ordinal), false, ct).ConfigureAwait(false),
            "GLEDSUSPENSE" => await SuspenseAsync(companyId, asAt, ct).ConfigureAwait(false),
            "GLEDRECURRINGDUE" => await RecurringDueAsync(companyId, asAt, ct).ConfigureAwait(false),
            "GLEDACCRUALS" => await AccrualsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "GLEDNOTES" => await NotesAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "GLEDMANAGEMENTPACK" => await ManagementPackAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "GLEDELIMINATIONS" => await EliminationsAsync(fromDate, toDate, ct).ConfigureAwait(false),
            "GLEDCONTROLLEGS" => await ControlLegsAsync(companyId, asAt, ct).ConfigureAwait(false),
            "GLEDYEAREND" => await YearEndAsync(companyId, asAt, ct).ConfigureAwait(false),
            "GLEDHYPERINFLATION" => await HyperinflationAsync(companyId, asAt, parameters, ct).ConfigureAwait(false),
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

    private async Task<Dictionary<string, decimal>> NetByAccountAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
        => await PostedLines(companyId, fromDate, toDate).GroupBy(l => l.AccountCode).Select(g => new { g.Key, Net = g.Sum(l => l.BaseDebit - l.BaseCredit) })
            .ToDictionaryAsync(g => g.Key, g => g.Net, ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> NotesAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<LedgerAccount> mapped = await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && a.NoteReference != null).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<string, decimal> opening = await NetByAccountAsync(companyId, DateOnly.MinValue, fromDate.AddDays(-1), ct).ConfigureAwait(false);
        Dictionary<string, decimal> movement = await NetByAccountAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (IGrouping<string, LedgerAccount> note in mapped.GroupBy(a => a.NoteReference!).OrderBy(g => g.Key, NoteOrder.Instance))
        {
            decimal noteOpening = 0m, noteMovement = 0m;
            foreach (LedgerAccount account in note.OrderBy(a => a.AccountCode, StringComparer.Ordinal))
            {
                decimal o = opening.GetValueOrDefault(account.AccountCode), m = movement.GetValueOrDefault(account.AccountCode);
                noteOpening += o;
                noteMovement += m;
                rows.Add(Row(("Note", note.Key), ("Account", account.AccountCode), ("Name", account.Name), ("Opening", Money(o)), ("Movement", Money(m)), ("Closing", o + m)));
            }

            rows.Add(Row(("Note", note.Key), ("Account", string.Empty), ("Name", $"Total note {note.Key}: {Money(noteOpening + noteMovement)}"), ("Opening", Money(noteOpening)), ("Movement", Money(noteMovement)),
                ("Closing", 0m)));
        }

        return rows;
    }

    // Orders note references numerically by part ("2" before "10", "7.2" before "7.10"), falling back to text.
    private sealed class NoteOrder : IComparer<string>
    {
        public static readonly NoteOrder Instance = new();

        public int Compare(string? x, string? y)
        {
            string[] a = (x ?? string.Empty).Split('.'), b = (y ?? string.Empty).Split('.');
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                int c = int.TryParse(a[i], CultureInfo.InvariantCulture, out int m) && int.TryParse(b[i], CultureInfo.InvariantCulture, out int n) ? m.CompareTo(n) : string.CompareOrdinal(a[i], b[i]);
                if (c != 0)
                {
                    return c;
                }
            }

            return a.Length.CompareTo(b.Length);
        }
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ManagementPackAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Core.System.FiscalYear? year = await _db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(y => y.CompanyId == companyId && y.StartDate <= toDate && y.EndDate >= toDate, ct).ConfigureAwait(false);
        DateOnly yearStart = year?.StartDate ?? new DateOnly(toDate.Year, 1, 1);
        List<LedgerAccount> accounts = await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<string, decimal> period = await NetByAccountAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);
        Dictionary<string, decimal> ytd = await NetByAccountAsync(companyId, yearStart, toDate, ct).ConfigureAwait(false);
        Dictionary<string, decimal> closing = await NetByAccountAsync(companyId, DateOnly.MinValue, toDate, ct).ConfigureAwait(false);
        decimal Sum(Dictionary<string, decimal> balances, Func<LedgerAccount, bool> filter) => accounts.Where(filter).Sum(a => balances.GetValueOrDefault(a.AccountCode));

        List<IReadOnlyDictionary<string, object?>> rows = [];
        void Add(string section, string item, string periodValue, string atValue) => rows.Add(Row(("Section", section), ("Item", item), ("Period", periodValue), ("YearToDate", atValue)));
        decimal incomeP = -Sum(period, a => a.Type == AccountType.Income), incomeY = -Sum(ytd, a => a.Type == AccountType.Income);
        decimal expenseP = Sum(period, a => a.Type == AccountType.Expense), expenseY = Sum(ytd, a => a.Type == AccountType.Expense);
        Add("Results", "Income", Money(incomeP), Money(incomeY));
        foreach (string category in accounts.Where(a => a.Type == AccountType.Expense).Select(a => a.ReportCategory).Distinct().Order(StringComparer.Ordinal))
        {
            decimal p = Sum(period, a => a.Type == AccountType.Expense && a.ReportCategory == category), y = Sum(ytd, a => a.Type == AccountType.Expense && a.ReportCategory == category);
            if (p != 0m || y != 0m)
            {
                Add("Results", $"Expenses — {(category.Length == 0 ? "other" : category)}", Money(p), Money(y));
            }
        }

        Add("Results", "Net profit / (loss)", Money(incomeP - expenseP), Money(incomeY - expenseY));
        Add("Results", "Net margin", Margin(incomeP, expenseP), Margin(incomeY, expenseY));
        decimal assets = Sum(closing, a => a.Type == AccountType.Asset), liabilities = -Sum(closing, a => a.Type == AccountType.Liability);
        decimal equity = -Sum(closing, a => a.Type is AccountType.Equity or AccountType.Income or AccountType.Expense);
        Add("Financial position", "Total assets", string.Empty, Money(assets));
        Add("Financial position", "Total liabilities", string.Empty, Money(liabilities));
        Add("Financial position", "Equity (including unclosed profit)", string.Empty, Money(equity));
        List<string> banks = await _db.BankAccounts.AsNoTracking().Where(b => b.CompanyId == companyId).Select(b => b.GlAccountCode).ToListAsync(ct).ConfigureAwait(false);
        Add("Working capital", "Trade receivables", string.Empty, Money(Sum(closing, a => a.ControlAccountType == ControlAccountType.AccountsReceivable)));
        Add("Working capital", "Trade payables", string.Empty, Money(-Sum(closing, a => a.ControlAccountType == ControlAccountType.AccountsPayable)));
        Add("Working capital", "Cash at bank (movement / balance)", Money(Sum(period, a => banks.Contains(a.AccountCode))), Money(Sum(closing, a => banks.Contains(a.AccountCode))));
        return rows;
    }

    private static string Margin(decimal income, decimal expense)
        => income == 0m ? "—" : ((income - expense) / income * 100m).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    // Group-wide: each intercompany document's journals in every company; it eliminates when two companies hold equal postings.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> EliminationsAsync(DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, string> companies = await _db.Companies.AsNoTracking().IgnoreQueryFilters().ToDictionaryAsync(c => c.Id, c => c.Code, ct).ConfigureAwait(false);
        var journals = await _db.Journals.AsNoTracking().IgnoreQueryFilters().Include(j => j.Lines)
            .Where(j => j.DocumentType == "Intercompany" && j.Status == JournalStatus.Posted && j.PostingDate >= fromDate && j.PostingDate <= toDate)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. journals.GroupBy(j => j.DocumentNumber ?? j.JournalNumber).OrderBy(g => g.Min(j => j.PostingDate)).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            List<JournalEntry> sides = [.. g.OrderBy(j => j.Id)];
            JournalEntry first = sides[0];
            JournalEntry? second = sides.Skip(1).FirstOrDefault(j => j.CompanyId != first.CompanyId);
            decimal a = first.Lines.Sum(l => l.BaseDebit), b = second?.Lines.Sum(l => l.BaseDebit) ?? 0m;
            string status = second is null ? "Unmatched" : a == b ? "Eliminated" : "Amounts differ";
            return Row(("Document", g.Key), ("Date", Date(first.PostingDate)), ("FromCompany", companies.GetValueOrDefault(first.CompanyId, "?")), ("FromAmount", Money(a)),
                ("ToCompany", second is null ? "—" : companies.GetValueOrDefault(second.CompanyId, "?")), ("ToAmount", second is null ? "—" : Money(b)), ("Status", status), ("Eliminated", status == "Eliminated" ? a : 0m));
        })];
    }

    private async Task<decimal> BalanceAsync(long companyId, string account, DateOnly asAt, CancellationToken ct)
        => await PostedLines(companyId, DateOnly.MinValue, asAt).Where(l => l.AccountCode == account).SumAsync(l => l.BaseDebit - l.BaseCredit, ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ControlLegsAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<IReadOnlyDictionary<string, object?>> rows = [];
        List<TaxCode> codes = await _db.TaxCodes.AsNoTracking().Where(t => t.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        foreach (string account in codes.SelectMany(c => new[] { c.OutputAccountCode, c.InputAccountCode }).Where(a => a != null).Select(a => a!).Distinct().Order(StringComparer.Ordinal))
        {
            decimal ledger = await BalanceAsync(companyId, account, asAt, ct).ConfigureAwait(false);
            decimal coded = await PostedLines(companyId, DateOnly.MinValue, asAt).Where(l => l.AccountCode == account && l.TaxCode != null).SumAsync(l => l.BaseDebit - l.BaseCredit, ct).ConfigureAwait(false);
            rows.Add(Row(("Control", "Tax"), ("Account", account), ("Ledger", Money(ledger)), ("Other", Money(coded)), ("Explained", "Tax-coded postings; the rest are settlements and manual entries"),
                ("Difference", ledger - coded)));
        }

        foreach (Core.Banking.BankAccount bank in await _db.BankAccounts.AsNoTracking().Where(b => b.CompanyId == companyId).OrderBy(b => b.Code).ToListAsync(ct).ConfigureAwait(false))
        {
            decimal ledger = await PostedLines(companyId, DateOnly.MinValue, asAt).Where(l => l.AccountCode == bank.GlAccountCode).SumAsync(l => l.TransactionDebit - l.TransactionCredit, ct).ConfigureAwait(false);
            decimal statement = await _db.BankStatementLines.AsNoTracking().Where(s => s.BankAccountId == bank.Id && s.StatementDate <= asAt).SumAsync(s => s.Amount, ct).ConfigureAwait(false);
            List<long> matched = await _db.BankStatementLines.AsNoTracking().Where(s => s.CompanyId == companyId && s.MatchedJournalLineId != null).Select(s => s.MatchedJournalLineId!.Value).ToListAsync(ct).ConfigureAwait(false);
            decimal unpresented = await PostedLines(companyId, DateOnly.MinValue, asAt).Where(l => l.AccountCode == bank.GlAccountCode && !matched.Contains(l.Id)).SumAsync(l => l.TransactionDebit - l.TransactionCredit, ct).ConfigureAwait(false);
            decimal unrecorded = await _db.BankStatementLines.AsNoTracking().Where(s => s.BankAccountId == bank.Id && s.StatementDate <= asAt && !s.IsReconciled).SumAsync(s => s.Amount, ct).ConfigureAwait(false);
            rows.Add(Row(("Control", $"Bank {bank.Code}"), ("Account", bank.GlAccountCode), ("Ledger", Money(ledger)), ("Other", Money(statement)),
                ("Explained", $"unreconciled ledger {Money(unpresented)} less unreconciled statement {Money(unrecorded)}"), ("Difference", ledger - statement - (unpresented - unrecorded))));
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> YearEndAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Core.System.FiscalYear? year = await _db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(y => y.CompanyId == companyId && y.StartDate <= asAt && y.EndDate >= asAt, ct).ConfigureAwait(false);
        if (year is null)
        {
            return [];
        }

        List<IReadOnlyDictionary<string, object?>> rows = [];
        void Add(string section, string item, string value) => rows.Add(Row(("Section", section), ("Item", item), ("Value", value)));
        Add("Year", $"{year.Code} ({Date(year.StartDate)} to {Date(year.EndDate)})", year.IsClosed ? "Closed" : "Open");
        var checklist = await _services.GetRequiredService<Application.Abstractions.Accounting.IPeriodCloseService>().ChecklistAsync(companyId, year.EndDate, ct).ConfigureAwait(false);
        foreach (var check in checklist.Checks)
        {
            Add("Close checklist", check.Name, check.Count == 0 ? "clear" : $"{check.Count} item(s), {Money(check.Value)}{(check.IsBlocking ? " — BLOCKING" : string.Empty)}");
        }

        List<LedgerAccount> accounts = await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        var balances = await PostedLines(companyId, DateOnly.MinValue, year.EndDate).GroupBy(l => l.AccountCode).Select(g => new { g.Key, Debit = g.Sum(l => l.BaseDebit), Credit = g.Sum(l => l.BaseCredit) })
            .ToListAsync(ct).ConfigureAwait(false);
        Add("Trial balance", "Total debits", Money(balances.Sum(b => Math.Max(0m, b.Debit - b.Credit))));
        Add("Trial balance", "Total credits", Money(balances.Sum(b => Math.Max(0m, b.Credit - b.Debit))));
        var movement = await PostedLines(companyId, year.StartDate, year.EndDate).GroupBy(l => l.AccountCode).Select(g => new { g.Key, Net = g.Sum(l => l.BaseDebit - l.BaseCredit) }).ToListAsync(ct).ConfigureAwait(false);
        decimal Of(AccountType type) => movement.Where(m => accounts.Any(a => a.AccountCode == m.Key && a.Type == type)).Sum(m => m.Net);
        decimal income = -Of(AccountType.Income), expense = Of(AccountType.Expense);
        Add("Result", "Income", Money(income));
        Add("Result", "Expenses", Money(expense));
        Add("Result", "Net profit for the year", Money(income - expense));
        foreach (var source in (await _db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.PostingDate >= year.StartDate && j.PostingDate <= year.EndDate)
                     .GroupBy(j => new { j.SourceModule, j.Status }).Select(g => new { g.Key.SourceModule, g.Key.Status, Count = g.Count() }).ToListAsync(ct).ConfigureAwait(false))
                     .OrderBy(s => s.SourceModule, StringComparer.Ordinal).ThenBy(s => s.Status))
        {
            Add("Journals", $"{source.SourceModule} {source.Status}", source.Count.ToString(CultureInfo.InvariantCulture));
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> HyperinflationAsync(long companyId, DateOnly asAt, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        string index = parameters.TryGetValue("IndexCode", out object? value) && value is string text && text.Trim().Length > 0 ? text.Trim() : "CPI";
        var restated = await _services.GetRequiredService<Application.Abstractions.Accounting.IHyperinflationService>().RestatedTrialBalanceAsync(companyId, asAt, index, ct).ConfigureAwait(false);
        return restated.IsFailure ? [] : [.. restated.Value.Rows.OrderBy(r => r.AccountCode, StringComparer.Ordinal)
            .Select(r => Row(("Account", r.AccountCode), ("Name", r.Name), ("Type", r.Type.ToString()), ("Monetary", r.Monetary ? "Yes" : "No"), ("Historical", Money(r.Historical)), ("Restated", Money(r.Restated)),
                ("Restatement", r.Restated - r.Historical)))];
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

    // Every period of the year up to the one holding the date, each with its module statuses and the close checklist at its end.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CloseStatusAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Core.System.FiscalYear? year = await _db.FiscalYears.AsNoTracking().Include(y => y.Periods).ThenInclude(p => p.ModuleStatuses)
            .FirstOrDefaultAsync(y => y.CompanyId == companyId && y.StartDate <= asAt && y.EndDate >= asAt, ct).ConfigureAwait(false);
        if (year is null)
        {
            return [];
        }

        var close = _services.GetRequiredService<Application.Abstractions.Accounting.IPeriodCloseService>();
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (Core.System.FiscalPeriod period in year.Periods.Where(p => p.StartDate <= asAt).OrderBy(p => p.PeriodNumber))
        {
            string status = period.ModuleStatuses.Count == 0 ? "—"
                : string.Join(", ", period.ModuleStatuses.OrderBy(m => m.ModuleCode, StringComparer.Ordinal).Select(m => $"{m.ModuleCode} {m.Status}"));
            var checklist = await close.ChecklistAsync(companyId, period.EndDate, ct).ConfigureAwait(false);
            string ready = checklist.IsReadyToClose ? (checklist.Outstanding == 0 ? "Yes" : "Yes (warnings)") : "No";
            rows.AddRange(checklist.Checks.Select(c => Row(("Period", period.Name), ("Ends", Date(period.EndDate)), ("Status", status), ("Ready", ready), ("Check", c.Name),
                ("Count", c.Count), ("Value", Money(c.Value)), ("Blocking", c.Passes ? "—" : c.IsBlocking ? "Blocks" : "Warns"), ("Detail", c.Detail))));
        }

        return rows;
    }
}
