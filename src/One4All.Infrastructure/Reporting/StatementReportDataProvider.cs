using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Core.Schools;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Trading;

namespace One4All.Infrastructure.Reporting;

/// <summary>
/// Rows for the statement reports (<c>StatementReports</c>, commands <c>Stmt*</c>): dunning letters, commission statements, the supplier,
/// ratepayer and student statements, and the supplier cash requirement forecast.
/// </summary>
public sealed class StatementReportDataProvider : IReportDataProvider
{
    private static readonly string[] Buckets = ["Current", "1-30 days", "31-60 days", "61-90 days", "Over 90 days"];

    private readonly One4AllDbContext _db;
    private readonly IDunningService? _dunning;
    private readonly ICommissionService? _commission;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="dunning">The dunning service (reminder levels and letters).</param>
    /// <param name="commission">The sales commission service.</param>
    public StatementReportDataProvider(One4AllDbContext db, IDunningService? dunning = null, ICommissionService? commission = null)
    {
        _db = db;
        _dunning = dunning;
        _commission = commission;
    }

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("Stmt", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly toDate = DateParameter(parameters, "ToDate") ?? today;
        DateOnly fromDate = DateParameter(parameters, "FromDate") ?? new DateOnly(toDate.Year, 1, 1);
        DateOnly asAt = DateParameter(parameters, "AsAtDate") ?? today;
        return command.ToUpperInvariant() switch
        {
            "STMTDUNNING" => await DunningAsync(companyId, asAt, parameters, ct).ConfigureAwait(false),
            "STMTCOMMISSION" => await CommissionAsync(companyId, fromDate, toDate, Text(parameters, "RepCode"), ct).ConfigureAwait(false),
            "STMTSUPPLIER" => await SupplierAsync(companyId, Text(parameters, "SupplierCode"), fromDate, toDate, ct).ConfigureAwait(false),
            "STMTCASHREQUIREMENT" => await CashRequirementAsync(companyId, asAt, Integer(parameters, "Weeks") ?? 8, ct).ConfigureAwait(false),
            "STMTRATEPAYER" => await RatepayerAsync(companyId, Text(parameters, "StandNumber"), fromDate, toDate, ct).ConfigureAwait(false),
            "STMTSTUDENT" => await StudentAsync(companyId, Text(parameters, "AdmissionNumber"), fromDate, toDate, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DunningAsync(long companyId, DateOnly asAt, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        if (_dunning is null)
        {
            return [];
        }

        DunningStages defaults = DunningStages.Default;
        DunningStages stages = new(Integer(parameters, "FirstDays") ?? defaults.FirstReminderDays, Integer(parameters, "SecondDays") ?? defaults.SecondReminderDays,
            Integer(parameters, "FinalDays") ?? defaults.FinalNoticeDays);
        Result<IReadOnlyList<DunningNotice>> notices = await _dunning.AssessAsync(companyId, asAt, stages, ct).ConfigureAwait(false);
        if (notices.IsFailure)
        {
            return [];
        }

        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (DunningNotice notice in notices.Value.OrderByDescending(n => n.Level).ThenBy(n => n.CustomerName, StringComparer.Ordinal))
        {
            Result<string> letter = await _dunning.RenderReminderTextAsync(companyId, notice.CustomerId, asAt, stages, ct: ct).ConfigureAwait(false);
            rows.Add(Row(("Customer", notice.CustomerName), ("Level", notice.Level), ("Stage", notice.LevelLabel), ("Overdue", notice.OverdueAmount), ("OldestDays", notice.OldestDaysOverdue),
                ("Letter", letter.IsSuccess ? letter.Value : string.Empty)));
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CommissionAsync(long companyId, DateOnly fromDate, DateOnly toDate, string? repCode, CancellationToken ct)
    {
        if (_commission is null)
        {
            return [];
        }

        string? code = repCode?.ToUpperInvariant();
        List<SalesRep> reps = await _db.Set<SalesRep>().AsNoTracking().Where(r => r.CompanyId == companyId && (code == null || r.Code.ToUpper() == code)).OrderBy(r => r.Code)
            .ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (SalesRep rep in reps)
        {
            Result<CommissionStatement> statement = await _commission.StatementAsync(companyId, rep.Id, fromDate, toDate, ct).ConfigureAwait(false);
            if (statement.IsFailure || statement.Value.Lines.Count == 0)
            {
                continue;
            }

            rows.AddRange(statement.Value.Lines.OrderBy(l => l.CustomerCode, StringComparer.Ordinal)
                .Select(l => Row(("Rep", rep.Code), ("Customer", l.CustomerCode), ("Base", l.Base), ("Commission", l.Commission))));
            rows.Add(Row(("Rep", rep.Code), ("Customer", $"Total to accrue - {rep.Name}"), ("Base", statement.Value.Lines.Sum(l => l.Base)), ("Commission", statement.Value.Total)));
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SupplierAsync(long companyId, string? code, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        string? wanted = code?.ToUpperInvariant();
        List<Supplier> suppliers = await _db.Suppliers.AsNoTracking().Where(s => s.CompanyId == companyId && (wanted == null || s.Code.ToUpper() == wanted)).OrderBy(s => s.Code)
            .ToListAsync(ct).ConfigureAwait(false);
        List<OpenItemAsAt> open = await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsPayable, toDate, ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (Supplier supplier in suppliers)
        {
            List<OpenItem> items = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable && i.TraderId == supplier.Id && i.DocumentDate <= toDate)
                .OrderBy(i => i.DocumentDate).ThenBy(i => i.Id).ToListAsync(ct).ConfigureAwait(false);
            if (items.Count > 0)
            {
                rows.AddRange(Statement($"{supplier.Code} {supplier.Name}", items.Select(i => new Movement(i.DocumentDate, i.ItemType.ToString(), i.DocumentNumber, Owed(i))), fromDate, toDate,
                    open.Where(o => o.Item.TraderId == supplier.Id).Select(o => (o.Item.DueDate, Owed(o.Item) < 0m ? -o.OutstandingBase : o.OutstandingBase))));
            }
        }

        return rows;

        // From our side, what we owe the supplier: their invoices are credits.
        static decimal Owed(OpenItem item) => item.Sign == LedgerSign.Credit ? item.OriginalBase : -item.OriginalBase;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CashRequirementAsync(long companyId, DateOnly asAt, int weeks, CancellationToken ct)
    {
        weeks = Math.Clamp(weeks, 1, 52);
        List<OpenItemAsAt> open = await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsPayable, asAt, ct).ConfigureAwait(false);
        int Bucket(DateOnly due) => due < asAt ? -1 : Math.Min((due.DayNumber - asAt.DayNumber) / 7, weeks);
        string Label(int bucket) => bucket < 0 ? "Overdue" : bucket == weeks ? "Later" : $"Week {bucket + 1}";
        return [.. open.Where(o => o.OutstandingBase != 0m || o.OutstandingTransaction != 0m)
            .GroupBy(o => (Currency: o.Item.Currency.Value, Bucket: Bucket(o.Item.DueDate))).OrderBy(g => g.Key.Currency, StringComparer.Ordinal).ThenBy(g => g.Key.Bucket)
            .Select(g =>
            {
                decimal Signed(OpenItemAsAt o, decimal amount) => o.Item.Sign == LedgerSign.Credit ? amount : -amount;
                return Row(("Currency", g.Key.Currency), ("Week", Label(g.Key.Bucket)),
                    ("From", g.Key.Bucket < 0 ? string.Empty : Date(asAt.AddDays(7 * g.Key.Bucket))), ("To", g.Key.Bucket < 0 ? Date(asAt.AddDays(-1)) : g.Key.Bucket == weeks ? string.Empty : Date(asAt.AddDays((7 * g.Key.Bucket) + 6))),
                    ("Suppliers", g.Select(o => o.Item.TraderId).Distinct().Count()), ("Documents", g.Count()),
                    ("Amount", g.Sum(o => Signed(o, o.OutstandingTransaction))), ("Base", g.Sum(o => Signed(o, o.OutstandingBase))));
            })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RatepayerAsync(long companyId, string? standNumber, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        string? stand = standNumber?.ToUpperInvariant();
        List<Property> properties = await _db.Properties.AsNoTracking().Include(p => p.Services)
            .Where(p => p.CompanyId == companyId && (stand == null || p.StandNumber.ToUpper() == stand)).OrderBy(p => p.StandNumber).ToListAsync(ct).ConfigureAwait(false);
        List<long> payers = [.. properties.Select(p => p.RatepayerCustomerId).Distinct()];
        Dictionary<long, Customer> customers = await _db.Customers.AsNoTracking().Where(c => payers.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        List<OpenItem> items = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && payers.Contains(i.TraderId) && i.DocumentDate <= toDate)
            .OrderBy(i => i.DocumentDate).ThenBy(i => i.Id).ToListAsync(ct).ConfigureAwait(false);
        List<OpenItemAsAt> open = await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, toDate, ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (Property property in properties)
        {
            string payer = customers.TryGetValue(property.RatepayerCustomerId, out Customer? customer) ? $"{customer.Code} {customer.Name}" : "?";
            string account = $"Stand {property.StandNumber} - {payer}";
            string services = string.Join(", ", property.Services.Where(s => s.IsActive).Select(s => s.ServiceType.ToString()).Distinct().Order(StringComparer.Ordinal));
            rows.Add(Row(("Account", account), ("Section", "Property"), ("Date", string.Empty), ("Type", property.Address ?? property.Ward), ("Document", services.Length == 0 ? "no services" : services),
                ("Debit", null), ("Credit", null), ("Balance", null)));
            rows.AddRange(Statement(account, items.Where(i => i.TraderId == property.RatepayerCustomerId)
                    .Select(i => new Movement(i.DocumentDate, ChargeType(i.ItemType), i.DocumentNumber, i.Sign == LedgerSign.Debit ? i.OriginalBase : -i.OriginalBase)), fromDate, toDate,
                open.Where(o => o.Item.TraderId == property.RatepayerCustomerId).Select(o => (o.Item.DueDate, o.Item.Sign == LedgerSign.Debit ? o.OutstandingBase : -o.OutstandingBase))));
        }

        return rows;

        static string ChargeType(OpenItemType type) => type switch
        {
            OpenItemType.Invoice => "Charges",
            OpenItemType.Receipt => "Payment",
            OpenItemType.Interest => "Interest",
            _ => type.ToString(),
        };
    }

    // Fees billed to each student (their invoices name them) and what has been allocated against those invoices.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StudentAsync(long companyId, string? admissionNumber, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        string? wanted = admissionNumber?.ToUpperInvariant();
        List<Student> students = await _db.Students.AsNoTracking().Where(s => s.CompanyId == companyId && (wanted == null || s.AdmissionNumber.ToUpper() == wanted))
            .OrderBy(s => s.Grade).ThenBy(s => s.FullName).ToListAsync(ct).ConfigureAwait(false);
        var bills = await (from i in _db.OpenItems.AsNoTracking()
                           join j in _db.Journals.AsNoTracking() on i.JournalEntryId equals j.Id
                           where i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.ItemType == OpenItemType.Invoice && i.DocumentDate <= toDate
                                 && j.Narrative != null && j.Narrative.Contains(" fees - ")
                           select new { Item = i, j.Narrative }).ToListAsync(ct).ConfigureAwait(false);
        var fees = bills.Select(b =>
        {
            int open = b.Narrative!.LastIndexOf('('), close = b.Narrative.LastIndexOf(')');
            return new { b.Item, Admission = open >= 0 && close > open ? b.Narrative[(open + 1)..close] : string.Empty, Term = b.Narrative[..b.Narrative.IndexOf(" fees - ", StringComparison.Ordinal)] };
        }).ToList();
        List<long> ids = [.. fees.Select(f => f.Item.Id)];
        var allocations = await (from a in _db.Allocations.AsNoTracking()
                                 join c in _db.OpenItems.AsNoTracking() on a.CreditItemId equals c.Id
                                 where ids.Contains(a.DebitItemId) && a.AllocationDate <= toDate && (!a.IsReversed || a.ReversedOn > toDate)
                                 select new { a.DebitItemId, a.AllocationDate, a.DebitBase, c.ItemType, c.DocumentNumber }).ToListAsync(ct).ConfigureAwait(false);
        List<long> payers = [.. students.Select(s => s.PayerCustomerId).Distinct()];
        Dictionary<long, string> names = await _db.Customers.AsNoTracking().Where(c => payers.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (Student student in students)
        {
            var mine = fees.Where(f => string.Equals(f.Admission, student.AdmissionNumber, StringComparison.OrdinalIgnoreCase)).ToList();
            if (mine.Count == 0)
            {
                continue;
            }

            HashSet<long> invoiceIds = [.. mine.Select(f => f.Item.Id)];
            var paid = allocations.Where(a => invoiceIds.Contains(a.DebitItemId)).ToList();
            IEnumerable<Movement> movements = mine.Select(f => new Movement(f.Item.DocumentDate, $"{f.Term} fees", f.Item.DocumentNumber, f.Item.OriginalBase))
                .Concat(paid.Select(a => new Movement(a.AllocationDate, a.ItemType.ToString(), a.DocumentNumber, -a.DebitBase)));
            IEnumerable<(DateOnly Due, decimal Outstanding)> outstanding = mine.Select(f => (f.Item.DueDate, f.Item.OriginalBase - paid.Where(a => a.DebitItemId == f.Item.Id).Sum(a => a.DebitBase)));
            string account = $"{student.FullName} ({student.AdmissionNumber}), {student.Grade} - billed to {names.GetValueOrDefault(student.PayerCustomerId, "?")}";
            rows.AddRange(Statement(account, movements.OrderBy(m => m.Date), fromDate, toDate, outstanding));
        }

        return rows;
    }

    private sealed record Movement(DateOnly Date, string Type, string Document, decimal Amount);

    // Opening balance, the period's movements with running balance, closing balance, then the outstanding aged by due date at the end.
    private static List<IReadOnlyDictionary<string, object?>> Statement(string account, IEnumerable<Movement> movements, DateOnly fromDate, DateOnly toDate,
        IEnumerable<(DateOnly Due, decimal Outstanding)> outstanding)
    {
        List<Movement> all = [.. movements];
        decimal balance = all.Where(m => m.Date < fromDate).Sum(m => m.Amount);
        List<IReadOnlyDictionary<string, object?>> rows = [Line("Opening balance", fromDate.AddDays(-1), string.Empty, string.Empty, null, balance)];
        foreach (Movement m in all.Where(m => m.Date >= fromDate && m.Date <= toDate))
        {
            balance += m.Amount;
            rows.Add(Line("Transactions", m.Date, m.Type, m.Document, m.Amount, balance));
        }

        rows.Add(Line("Closing balance", toDate, string.Empty, string.Empty, null, balance));
        decimal[] aged = new decimal[Buckets.Length];
        foreach ((DateOnly due, decimal amount) in outstanding)
        {
            int days = toDate.DayNumber - due.DayNumber;
            aged[days <= 0 ? 0 : days <= 30 ? 1 : days <= 60 ? 2 : days <= 90 ? 3 : 4] += amount;
        }

        rows.AddRange(Buckets.Select((label, i) => Line("Ageing", toDate, label, string.Empty, null, aged[i])));
        return rows;

        IReadOnlyDictionary<string, object?> Line(string section, DateOnly date, string type, string document, decimal? amount, decimal running) => Row(("Account", account), ("Section", section),
            ("Date", Date(date)), ("Type", type), ("Document", document), ("Debit", amount > 0m ? amount : null), ("Credit", amount < 0m ? -amount : null), ("Balance", running));
    }

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells)
    {
        Dictionary<string, object?> row = new(StringComparer.Ordinal);
        foreach ((string key, object? value) in cells)
        {
            row[key] = value;
        }

        return row;
    }

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Text(IReadOnlyDictionary<string, object?> parameters, string name)
        => parameters.TryGetValue(name, out object? value) && value is string text && text.Trim().Length > 0 ? text.Trim() : null;

    private static int? Integer(IReadOnlyDictionary<string, object?> parameters, string name) => parameters.TryGetValue(name, out object? value) ? value switch
    {
        int i => i,
        long l => (int)l,
        decimal d => (int)d,
        string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) => parsed,
        _ => null,
    } : null;

    private static DateOnly? DateParameter(IReadOnlyDictionary<string, object?> parameters, string name) => parameters.TryGetValue(name, out object? value) && value is DateOnly date ? date : null;
}
