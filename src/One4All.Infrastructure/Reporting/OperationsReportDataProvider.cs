using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Housing;
using One4All.Core.Accounting;
using One4All.Core.JobCosting;
using One4All.Core.SalesOrders;
using One4All.Core.Schools;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Trading;

namespace One4All.Infrastructure.Reporting;

/// <summary>Serves the SC-, JC- and SO-numbered reports (brief §14 J, K, H): every data command whose name starts with <c>Ops</c>.</summary>
public sealed class OperationsReportDataProvider : IReportDataProvider
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    public OperationsReportDataProvider(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("Ops", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly asAt = DateParameter(parameters, "AsAtDate") ?? today;
        DateOnly toDate = DateParameter(parameters, "ToDate") ?? today;
        DateOnly fromDate = DateParameter(parameters, "FromDate") ?? new DateOnly(toDate.Year, 1, 1);
        return command.ToUpperInvariant() switch
        {
            "OPSFEEBILLING" => await FeeBillingAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "OPSFEEARREARS" => await FeeArrearsAsync(companyId, asAt, ct).ConfigureAwait(false),
            "OPSFEECOLLECTIONS" => await FeeCollectionsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "OPSBURSARIES" => await BursariesAsync(companyId, ct).ConfigureAwait(false),
            "OPSENROLMENT" => await EnrolmentAsync(companyId, ct).ConfigureAwait(false),
            "OPSSTUDENTBALANCES" => await StudentBalancesAsync(companyId, asAt, ct).ConfigureAwait(false),
            "OPSJOBCOSTS" => await JobCostsAsync(companyId, ct).ConfigureAwait(false),
            "OPSCOSTTOCOMPLETE" => await CostToCompleteAsync(companyId, ct).ConfigureAwait(false),
            "OPSRETENTION" => await RetentionAsync(companyId, ct).ConfigureAwait(false),
            "OPSVARIATIONS" => await VariationsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "OPSTIMESHEETS" => await TimesheetsAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "OPSBOQ" => await BoqAsync(companyId, ct).ConfigureAwait(false),
            "OPSCONTRACTCASH" => await ContractCashAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            "OPSOPENORDERS" => await OpenOrdersAsync(companyId, ct).ConfigureAwait(false),
            "OPSQUOTECONVERSION" => await QuoteConversionAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private sealed record FeeInvoice(long OpenItemId, string Admission, string Term, DateOnly Date);

    // School bills are AR invoices whose journal narrative reads "{term} fees — {name} ({admission number})".
    private async Task<List<FeeInvoice>> FeeInvoicesAsync(long companyId, CancellationToken ct)
    {
        var bills = await (from i in _db.OpenItems.AsNoTracking() join j in _db.Journals.AsNoTracking() on i.JournalEntryId equals j.Id
                           where i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.ItemType == OpenItemType.Invoice && j.Narrative != null && j.Narrative.Contains(" fees — ")
                           select new { i.Id, j.Narrative, i.DocumentDate }).ToListAsync(ct).ConfigureAwait(false);
        return [.. bills.Select(b =>
        {
            int open = b.Narrative!.LastIndexOf('('), close = b.Narrative.LastIndexOf(')');
            string admission = open >= 0 && close > open ? b.Narrative[(open + 1)..close] : string.Empty;
            string term = b.Narrative[..b.Narrative.IndexOf(" fees — ", StringComparison.Ordinal)];
            return new FeeInvoice(b.Id, admission, term, b.DocumentDate);
        }).Where(f => f.Admission.Length > 0)];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FeeBillingAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<string, Student> students = await StudentsAsync(companyId, ct).ConfigureAwait(false);
        List<FeeInvoice> invoices = [.. (await FeeInvoicesAsync(companyId, ct).ConfigureAwait(false)).Where(f => f.Date >= fromDate && f.Date <= toDate && students.ContainsKey(f.Admission))];
        List<long> ids = [.. invoices.Select(f => f.OpenItemId)];
        Dictionary<long, decimal> amounts = await _db.OpenItems.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.OriginalBase, ct).ConfigureAwait(false);
        return [.. invoices.GroupBy(f => (f.Term, students[f.Admission].Grade)).OrderBy(g => g.Key.Term, StringComparer.Ordinal).ThenBy(g => g.Key.Grade, StringComparer.Ordinal)
            .Select(g => Row(("Term", g.Key.Term), ("Grade", g.Key.Grade), ("Students", g.Select(f => f.Admission).Distinct().Count()),
                ("Boarders", g.Select(f => f.Admission).Distinct().Count(a => students[a].IsBoarder)), ("Billed", g.Sum(f => amounts.GetValueOrDefault(f.OpenItemId)))))];
    }

    // Each student's fee invoices still open at the date.
    private async Task<Dictionary<string, List<OpenItemAsAt>>> OpenFeesAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<FeeInvoice> invoices = await FeeInvoicesAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> byItem = invoices.ToDictionary(f => f.OpenItemId, f => f.Admission);
        return (await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, asAt, ct).ConfigureAwait(false))
            .Where(i => byItem.ContainsKey(i.Item.Id) && i.OutstandingBase != 0m).GroupBy(i => byItem[i.Item.Id]).ToDictionary(g => g.Key, g => g.ToList());
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FeeArrearsAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<string, Student> students = await StudentsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> guardians = await GuardiansAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<string, List<OpenItemAsAt>> open = await OpenFeesAsync(companyId, asAt, ct).ConfigureAwait(false);
        return [.. open.Where(o => students.ContainsKey(o.Key)).Select(o => (Student: students[o.Key], Items: o.Value, Oldest: o.Value.Min(i => i.Item.DueDate)))
            .Where(x => x.Oldest < asAt).OrderBy(x => x.Student.Grade, StringComparer.Ordinal).ThenBy(x => x.Student.FullName, StringComparer.Ordinal)
            .Select(x => Row(("Grade", x.Student.Grade), ("Student", $"{x.Student.FullName} ({x.Student.AdmissionNumber})"), ("Guardian", guardians.GetValueOrDefault(x.Student.GuardianCustomerId, "?")),
                ("Invoices", x.Items.Count), ("Oldest", Date(x.Oldest)), ("Band", DebtorAgeing.Bands[DebtorAgeing.Band(x.Oldest, asAt)]), ("Outstanding", x.Items.Sum(i => i.OutstandingBase))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FeeCollectionsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<Student> students = [.. (await StudentsAsync(companyId, ct).ConfigureAwait(false)).Values];
        HashSet<long> guardianIds = [.. students.Select(s => s.GuardianCustomerId)];
        List<long> ids = [.. guardianIds];
        Dictionary<long, string> guardians = await GuardiansAsync(companyId, ct).ConfigureAwait(false);
        List<OpenItem> receipts = await _db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.ItemType == OpenItemType.Receipt
            && ids.Contains(i.TraderId) && i.DocumentDate >= fromDate && i.DocumentDate <= toDate).OrderBy(i => i.DocumentDate).ThenBy(i => i.DocumentNumber).ToListAsync(ct).ConfigureAwait(false);
        return [.. receipts.Select(r => Row(("Date", Date(r.DocumentDate)), ("Receipt", r.DocumentNumber), ("Guardian", guardians.GetValueOrDefault(r.TraderId, "?")),
            ("Students", string.Join(", ", students.Where(s => s.GuardianCustomerId == r.TraderId).Select(s => s.FullName).Order(StringComparer.Ordinal))), ("Amount", r.OriginalBase)))];
    }

    // A bursary is worth its percentage of, or its fixed value against, the compulsory fees of the student's grade (the latest structure).
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BursariesAsync(long companyId, CancellationToken ct)
    {
        List<Student> students = [.. (await StudentsAsync(companyId, ct).ConfigureAwait(false)).Values.Where(s => s.BursaryType != BursaryType.None)];
        Dictionary<string, decimal> fees = (await _db.FeeStructures.AsNoTracking().Include(f => f.Items).Where(f => f.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(f => f.Grade).ToDictionary(g => g.Key, g => g.OrderBy(f => f.TermStart).ThenBy(f => f.Id).Last().Items.Where(i => i.IsCompulsory).Sum(i => i.Amount));
        return [.. students.OrderBy(s => s.Grade, StringComparer.Ordinal).ThenBy(s => s.FullName, StringComparer.Ordinal).Select(s =>
        {
            decimal gradeFees = fees.GetValueOrDefault(s.Grade);
            decimal worth = s.BursaryType == BursaryType.Percentage ? Math.Round(gradeFees * s.BursaryValue / 100m, 2, MidpointRounding.AwayFromZero) : Math.Min(s.BursaryValue, gradeFees);
            return Row(("Student", $"{s.FullName} ({s.AdmissionNumber})"), ("Grade", s.Grade), ("Type", s.BursaryType.ToString()),
                ("Rate", s.BursaryType == BursaryType.Percentage ? $"{s.BursaryValue:0.##}%" : Money(s.BursaryValue)), ("Fees", Money(gradeFees)), ("Worth", worth));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> EnrolmentAsync(long companyId, CancellationToken ct)
        => [.. (await StudentsAsync(companyId, ct).ConfigureAwait(false)).Values.GroupBy(s => s.Grade).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => Row(("Grade", g.Key), ("Enrolled", g.Count(s => s.IsEnrolled)), ("Boarders", g.Count(s => s.IsEnrolled && s.IsBoarder)), ("Day", g.Count(s => s.IsEnrolled && !s.IsBoarder)),
                ("Bursary", g.Count(s => s.IsEnrolled && s.BursaryType != BursaryType.None)), ("Left", g.Count(s => !s.IsEnrolled))))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StudentBalancesAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<string, Student> students = await StudentsAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> guardians = await GuardiansAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<string, List<OpenItemAsAt>> open = await OpenFeesAsync(companyId, asAt, ct).ConfigureAwait(false);
        return [.. students.Values.OrderBy(s => s.Grade, StringComparer.Ordinal).ThenBy(s => s.FullName, StringComparer.Ordinal)
            .Select(s => Row(("Admission", s.AdmissionNumber), ("Student", s.FullName), ("Grade", s.Grade), ("Guardian", guardians.GetValueOrDefault(s.GuardianCustomerId, "?")),
                ("Status", s.IsEnrolled ? "Enrolled" : "Left"), ("Balance", open.TryGetValue(s.AdmissionNumber, out List<OpenItemAsAt>? items) ? items.Sum(i => i.OutstandingBase) : 0m)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> JobCostsAsync(long companyId, CancellationToken ct)
    {
        Dictionary<long, JobContract> contracts = await _db.JobContracts.AsNoTracking().Where(c => c.CompanyId == companyId).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        var costs = await _db.Set<JobCostEntry>().AsNoTracking().Where(e => e.CompanyId == companyId).GroupBy(e => new { e.JobContractId, e.CostHead, e.Source })
            .Select(g => new { g.Key.JobContractId, g.Key.CostHead, g.Key.Source, Amount = g.Sum(e => e.Amount) }).ToListAsync(ct).ConfigureAwait(false);
        return [.. costs.Where(c => contracts.ContainsKey(c.JobContractId)).GroupBy(c => c.JobContractId).OrderBy(g => contracts[g.Key].ContractNumber, StringComparer.Ordinal).SelectMany(g =>
        {
            decimal total = g.Sum(c => c.Amount);
            return g.OrderByDescending(c => c.Amount).Select(c => Row(("Contract", contracts[g.Key].ContractNumber), ("Name", contracts[g.Key].Name), ("CostHead", c.CostHead), ("Source", c.Source.ToString()),
                ("Share", total == 0m ? "—" : (c.Amount * 100m / total).ToString("0.0", CultureInfo.InvariantCulture) + "%"), ("Cost", c.Amount)));
        })];
    }

    // % complete by certification (certified ÷ contract value); forecast final cost = cost to date ÷ % complete (budget until anything is certified).
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CostToCompleteAsync(long companyId, CancellationToken ct)
    {
        List<JobContract> contracts = await _db.JobContracts.AsNoTracking().Where(c => c.CompanyId == companyId).OrderBy(c => c.ContractNumber).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, decimal> actual = await _db.Set<JobCostEntry>().AsNoTracking().Where(e => e.CompanyId == companyId).GroupBy(e => e.JobContractId)
            .Select(g => new { g.Key, Amount = g.Sum(e => e.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Amount, ct).ConfigureAwait(false);
        return [.. contracts.Select(c =>
        {
            decimal cost = actual.GetValueOrDefault(c.Id);
            decimal complete = c.ContractValue == 0m ? 0m : Math.Min(1m, c.CertifiedToDate / c.ContractValue);
            decimal forecast = complete == 0m ? Math.Max(c.BudgetCost, cost) : Math.Round(cost / complete, 2, MidpointRounding.AwayFromZero);
            return Row(("Contract", c.ContractNumber), ("Name", c.Name), ("Value", Money(c.ContractValue)), ("Budget", Money(c.BudgetCost)), ("Actual", Money(cost)),
                ("Complete", (complete * 100m).ToString("0.0", CultureInfo.InvariantCulture) + "%"), ("Forecast", Money(forecast)), ("Variance", forecast - c.BudgetCost));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RetentionAsync(long companyId, CancellationToken ct)
    {
        Dictionary<long, string> customers = await GuardiansAsync(companyId, ct).ConfigureAwait(false);
        return [.. (await _db.JobContracts.AsNoTracking().Where(c => c.CompanyId == companyId && c.RetentionHeld != 0m).OrderBy(c => c.ContractNumber).ToListAsync(ct).ConfigureAwait(false))
            .Select(c => Row(("Contract", c.ContractNumber), ("Name", c.Name), ("Customer", customers.GetValueOrDefault(c.CustomerId, "?")), ("Status", c.Status.ToString()),
                ("Rate", c.RetentionPercent.ToString("0.##", CultureInfo.InvariantCulture)), ("Certified", Money(c.CertifiedToDate)), ("Retention", c.RetentionHeld)))];
    }

    private Task<Dictionary<long, string>> ContractNumbersAsync(long companyId, CancellationToken ct)
        => _db.JobContracts.AsNoTracking().IgnoreQueryFilters().Where(c => c.CompanyId == companyId).ToDictionaryAsync(c => c.Id, c => c.ContractNumber, ct);

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> VariationsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, string> contracts = await ContractNumbersAsync(companyId, ct).ConfigureAwait(false);
        List<Core.JobCosting.VariationOrder> variations = await _db.VariationOrders.AsNoTracking().Where(v => v.CompanyId == companyId && v.SubmittedOn >= fromDate && v.SubmittedOn <= toDate)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. variations.OrderBy(v => contracts.GetValueOrDefault(v.JobContractId), StringComparer.Ordinal).ThenBy(v => v.Number, StringComparer.Ordinal)
            .Select(v => Row(("Contract", contracts.GetValueOrDefault(v.JobContractId, "?")), ("Number", v.Number), ("Description", v.Description), ("Submitted", Date(v.SubmittedOn)), ("Status", v.Status.ToString()),
                ("Decided", v.DecidedOn is DateOnly d ? Date(d) : "—"), ("CostImpact", Money(v.CostImpact)), ("ValueImpact", v.ValueImpact)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TimesheetsAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        Dictionary<long, string> contracts = await ContractNumbersAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> employees = await _db.Set<Core.Payroll.Employee>().AsNoTracking().Where(e => e.CompanyId == companyId)
            .ToDictionaryAsync(e => e.Id, e => e.EmployeeNumber + " " + e.FullName, ct).ConfigureAwait(false);
        List<Core.JobCosting.JobTimesheet> sheets = await _db.JobTimesheets.AsNoTracking().Where(t => t.CompanyId == companyId && t.Date >= fromDate && t.Date <= toDate).ToListAsync(ct).ConfigureAwait(false);
        return [.. sheets.GroupBy(t => (t.JobContractId, t.EmployeeId, t.CostHead)).OrderBy(g => contracts.GetValueOrDefault(g.Key.JobContractId), StringComparer.Ordinal)
            .ThenBy(g => employees.GetValueOrDefault(g.Key.EmployeeId), StringComparer.Ordinal).Select(g =>
            {
                decimal hours = g.Sum(t => t.Hours), cost = g.Sum(t => t.Cost);
                return Row(("Contract", contracts.GetValueOrDefault(g.Key.JobContractId, "?")), ("Employee", employees.GetValueOrDefault(g.Key.EmployeeId, "?")), ("CostHead", g.Key.CostHead),
                    ("Days", g.Select(t => t.Date).Distinct().Count()), ("Hours", hours.ToString("0.##", CultureInfo.InvariantCulture)), ("Rate", hours == 0m ? "—" : Money(cost / hours)), ("Cost", cost));
            })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BoqAsync(long companyId, CancellationToken ct)
    {
        Dictionary<long, string> contracts = await ContractNumbersAsync(companyId, ct).ConfigureAwait(false);
        List<Core.JobCosting.BoqItem> items = await _db.BoqItems.AsNoTracking().Where(b => b.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        return [.. items.OrderBy(b => contracts.GetValueOrDefault(b.JobContractId), StringComparer.Ordinal).ThenBy(b => b.ItemCode, StringComparer.Ordinal)
            .Select(b => Row(("Contract", contracts.GetValueOrDefault(b.JobContractId, "?")), ("Item", b.ItemCode), ("Description", b.Description), ("Unit", b.Unit),
                ("Billed", b.Quantity.ToString("0.###", CultureInfo.InvariantCulture)), ("Measured", b.MeasuredQuantity.ToString("0.###", CultureInfo.InvariantCulture)),
                ("Variance", (b.MeasuredQuantity - b.Quantity).ToString("0.###", CultureInfo.InvariantCulture)),
                ("Complete", (b.MeasuredQuantity * 100m / b.Quantity).ToString("0.0", CultureInfo.InvariantCulture) + "%"), ("Value", Math.Round(b.MeasuredQuantity * b.Rate, 2))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ContractCashAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<JobContract> contracts = await _db.JobContracts.AsNoTracking().Where(c => c.CompanyId == companyId).OrderBy(c => c.ContractNumber).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, decimal> certified = await _db.ProgressCertificates.AsNoTracking().Where(p => p.CompanyId == companyId && p.Date >= fromDate && p.Date <= toDate).GroupBy(p => p.JobContractId)
            .Select(g => new { g.Key, Net = g.Sum(p => p.NetPayable) }).ToDictionaryAsync(x => x.Key, x => x.Net, ct).ConfigureAwait(false);
        Dictionary<long, decimal> costs = await _db.Set<JobCostEntry>().AsNoTracking().Where(e => e.CompanyId == companyId && e.Date >= fromDate && e.Date <= toDate).GroupBy(e => e.JobContractId)
            .Select(g => new { g.Key, Amount = g.Sum(e => e.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Amount, ct).ConfigureAwait(false);
        return [.. contracts.Where(c => certified.ContainsKey(c.Id) || costs.ContainsKey(c.Id)).Select(c => Row(("Contract", c.ContractNumber), ("Name", c.Name),
            ("Certified", Money(certified.GetValueOrDefault(c.Id))), ("Costs", Money(costs.GetValueOrDefault(c.Id))), ("Net", certified.GetValueOrDefault(c.Id) - costs.GetValueOrDefault(c.Id))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> OpenOrdersAsync(long companyId, CancellationToken ct)
    {
        Dictionary<long, string> customers = await GuardiansAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> items = await _db.Items.AsNoTracking().IgnoreQueryFilters().Where(i => i.CompanyId == companyId).ToDictionaryAsync(i => i.Id, i => i.Code + " " + i.Description, ct).ConfigureAwait(false);
        Dictionary<long, decimal> onHand = await _db.StockBalances.AsNoTracking().Where(b => b.CompanyId == companyId).GroupBy(b => b.ItemId)
            .Select(g => new { g.Key, Qty = g.Sum(b => b.QuantityOnHand) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct).ConfigureAwait(false);
        List<SalesOrder> orders = await _db.SalesOrders.AsNoTracking().Include(o => o.Lines)
            .Where(o => o.CompanyId == companyId && (o.Status == SalesOrderStatus.Open || o.Status == SalesOrderStatus.PartDespatched)).OrderBy(o => o.OrderDate).ThenBy(o => o.OrderNumber).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, decimal> free = new(onHand);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (SalesOrder order in orders)
        {
            foreach (SalesOrderLine line in order.Lines.Where(l => l.OrderedQuantity > l.DespatchedQuantity))
            {
                decimal outstanding = line.OrderedQuantity - line.DespatchedQuantity;
                decimal available = Math.Max(0m, free.GetValueOrDefault(line.ItemId));
                decimal covered = Math.Min(available, outstanding);
                free[line.ItemId] = available - covered;
                rows.Add(Row(("Order", order.OrderNumber), ("Date", Date(order.OrderDate)), ("Customer", customers.GetValueOrDefault(order.CustomerId, "?")), ("Item", items.GetValueOrDefault(line.ItemId, line.Description)),
                    ("Ordered", Qty(line.OrderedQuantity)), ("Despatched", Qty(line.DespatchedQuantity)), ("Outstanding", Qty(outstanding)), ("Free", Qty(available)),
                    ("Cover", covered == outstanding ? "Covered" : covered == 0m ? "No stock" : $"Short {Qty(outstanding - covered)}"),
                    ("Value", Math.Round(outstanding * line.UnitPrice, 2, MidpointRounding.AwayFromZero))));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QuoteConversionAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        List<Quotation> quotes = await _db.Quotations.AsNoTracking().Include(q => q.Lines).Where(q => q.CompanyId == companyId && q.QuoteDate >= fromDate && q.QuoteDate <= toDate).ToListAsync(ct).ConfigureAwait(false);
        static decimal Value(Quotation q) => q.Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero));
        return [.. quotes.GroupBy(q => q.QuoteDate.ToString("yyyy-MM", CultureInfo.InvariantCulture)).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            List<Quotation> converted = [.. g.Where(q => q.Status == QuotationStatus.Converted || q.SalesOrderId != null)];
            return Row(("Month", g.Key), ("Quotes", g.Count()), ("Converted", converted.Count), ("Rate", (converted.Count * 100m / g.Count()).ToString("0.0", CultureInfo.InvariantCulture) + "%"),
                ("Quoted", Money(g.Sum(Value))), ("ConvertedValue", converted.Sum(Value)));
        })];
    }

    private Task<Dictionary<string, Student>> StudentsAsync(long companyId, CancellationToken ct)
        => _db.Students.AsNoTracking().IgnoreQueryFilters().Where(s => s.CompanyId == companyId).ToDictionaryAsync(s => s.AdmissionNumber, ct);

    private Task<Dictionary<long, string>> GuardiansAsync(long companyId, CancellationToken ct)
        => _db.Customers.AsNoTracking().IgnoreQueryFilters().Where(c => c.CompanyId == companyId).ToDictionaryAsync(c => c.Id, c => c.Name, ct);

    private static DateOnly? DateParameter(IReadOnlyDictionary<string, object?> parameters, string name) => parameters.TryGetValue(name, out object? value) && value is DateOnly date ? date : null;

    private static string Qty(decimal quantity) => quantity.ToString("0.###", CultureInfo.InvariantCulture);

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
