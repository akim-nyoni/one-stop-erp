using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Budgeting;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>Serves the PO-numbered procurement reports (brief §14 G): every data command whose name starts with <c>Proc</c>.</summary>
public sealed class ProcurementReportDataProvider : IReportDataProvider
{
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FrameworksAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<FrameworkAgreement> agreements = await _db.FrameworkAgreements.AsNoTracking().Where(a => a.CompanyId == companyId).OrderBy(a => a.Reference).ToListAsync(ct).ConfigureAwait(false);
        List<long> ids = [.. agreements.Select(a => a.Id)];
        List<PurchaseOrder> orders = await _db.Set<PurchaseOrder>().AsNoTracking().Include(o => o.Lines)
            .Where(o => o.FrameworkAgreementId != null && ids.Contains(o.FrameworkAgreementId.Value) && o.Status != PurchaseOrderStatus.Cancelled && o.OrderDate <= asAt).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> suppliers = await _db.Suppliers.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(s => s.CompanyId == companyId).ToDictionaryAsync(s => s.Id, s => s.Name, ct).ConfigureAwait(false);
        return [.. agreements.Select(a =>
        {
            List<PurchaseOrder> own = [.. orders.Where(o => o.FrameworkAgreementId == a.Id)];
            decimal called = own.Sum(o => o.TotalNet), remaining = a.CeilingValue - called;
            string status = asAt < a.StartDate ? "Not started" : asAt > a.EndDate ? "Expired" : remaining <= 0m ? "Exhausted" : "Live";
            return Row(("Agreement", a.Reference), ("Supplier", suppliers.GetValueOrDefault(a.SupplierId, "?")),
                ("Term", $"{a.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} to {a.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"), ("Ceiling", Money(a.CeilingValue)), ("Orders", own.Count),
                ("Called", Money(called)), ("Used", a.CeilingValue == 0m ? "—" : (called * 100m / a.CeilingValue).ToString("0.0", CultureInfo.InvariantCulture) + "%"), ("Status", status), ("Remaining", remaining));
        })];
    }

    // Lines are checked in requisition order, each against what is left after the lines before it on the same vote.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BudgetCheckAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Core.System.FiscalPeriod? period = await (from p in _db.Set<Core.System.FiscalPeriod>().AsNoTracking()
                                                  join y in _db.FiscalYears.AsNoTracking() on p.FiscalYearId equals y.Id
                                                  where y.CompanyId == companyId && p.StartDate <= asAt && p.EndDate >= asAt
                                                  select p).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (period is null)
        {
            return [];
        }

        Dictionary<string, Application.Abstractions.Budgeting.VoteBookRow> votes = (await _budgets.VoteBookAsync(companyId, period.FiscalYearId, period.PeriodNumber, ct).ConfigureAwait(false))
            .GroupBy(v => v.AccountCode).ToDictionary(g => g.Key, g => g.First());
        PurchaseRequisitionStatus[] open = [PurchaseRequisitionStatus.Draft, PurchaseRequisitionStatus.Submitted, PurchaseRequisitionStatus.Approved];
        List<PurchaseRequisition> requisitions = await _db.Set<PurchaseRequisition>().AsNoTracking().Include(r => r.Lines)
            .Where(r => r.CompanyId == companyId && open.Contains(r.Status) && r.PurchaseOrderId == null && r.RequestedDate <= asAt).OrderBy(r => r.RequestedDate).ThenBy(r => r.RequisitionNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<string, decimal> used = [];
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (PurchaseRequisition r in requisitions)
        {
            foreach (PurchaseRequisitionLine line in r.Lines.Where(l => l.ExpenseAccountCode != null))
            {
                string account = line.ExpenseAccountCode!;
                decimal value = line.Quantity * line.UnitPrice;
                Application.Abstractions.Budgeting.VoteBookRow? vote = votes.GetValueOrDefault(account);
                decimal before = (vote?.Unexpended ?? 0m) - used.GetValueOrDefault(account), after = before - value;
                used[account] = used.GetValueOrDefault(account) + value;
                rows.Add(Row(("Requisition", r.RequisitionNumber), ("Date", r.RequestedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    ("Account", vote is null ? $"{account} (no budget)" : $"{account} {vote.Name}"), ("Line", Money(value)), ("Appropriation", Money(vote?.Appropriation ?? 0m)), ("Spent", Money(vote?.Expenditure ?? 0m)),
                    ("Committed", Money(vote?.Commitments ?? 0m)), ("Before", Money(before)), ("Status", vote is null ? "No budget" : after >= 0m ? "Within budget" : "Exceeds budget"), ("After", after)));
            }
        }

        return rows;
    }

    private static readonly PurchaseOrderStatus[] Done = [PurchaseOrderStatus.Received, PurchaseOrderStatus.Closed, PurchaseOrderStatus.Cancelled];

    private readonly One4AllDbContext _db;
    private readonly Application.Abstractions.Budgeting.IBudgetControlService _budgets;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="budgets">The vote book, for the budget check at requisition.</param>
    public ProcurementReportDataProvider(One4AllDbContext db, Application.Abstractions.Budgeting.IBudgetControlService budgets)
    {
        _db = db;
        _budgets = budgets;
    }

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("Proc", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly to = DateParameter(parameters, "ToDate") ?? today;
        DateOnly fromDate = DateParameter(parameters, "FromDate") ?? new DateOnly(to.Year, 1, 1);
        DateOnly asAt = DateParameter(parameters, "AsAtDate") ?? to;
        return command.ToUpperInvariant() switch
        {
            "PROCFRAMEWORKS" => await FrameworksAsync(companyId, DateParameter(parameters, "AsAtDate") ?? DateOnly.FromDateTime(DateTime.Today), ct).ConfigureAwait(false),
            "PROCBUDGETCHECK" => await BudgetCheckAsync(companyId, DateParameter(parameters, "AsAtDate") ?? DateOnly.FromDateTime(DateTime.Today), ct).ConfigureAwait(false),
            "PROCREQUISITIONTURNAROUND" => await RequisitionTurnaroundAsync(companyId, fromDate, to, ct).ConfigureAwait(false),
            "PROCAPPROVALHISTORY" => await ApprovalHistoryAsync(companyId, fromDate, to, ct).ConfigureAwait(false),
            "PROCAPPROVALSLA" => await ApprovalSlaAsync(companyId, fromDate, to, ct).ConfigureAwait(false),
            "PROCORDERS" => await OrdersAsync(companyId, fromDate, to, asAt, ct).ConfigureAwait(false),
            "PROCCOMMITMENTS" => await CommitmentsAsync(companyId, ct).ConfigureAwait(false),
            "PROCGOODSRECEIVED" => await GoodsReceivedAsync(companyId, fromDate, to, ct).ConfigureAwait(false),
            "PROCREJECTIONS" => await RejectionsAsync(companyId, fromDate, to, ct).ConfigureAwait(false),
            "PROCRFQS" => await RfqsAsync(companyId, ct).ConfigureAwait(false),
            "PROCPRICEVARIANCE" => await PriceVarianceAsync(companyId, fromDate, to, ct).ConfigureAwait(false),
            "PROCSPEND" => await SpendAsync(companyId, fromDate, to, ct).ConfigureAwait(false),
            "PROCLEADTIMES" => await LeadTimesAsync(companyId, fromDate, to, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    // Approval date: the last approving workflow action on the requisition, else when the approved record was last changed.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RequisitionTurnaroundAsync(long companyId, DateOnly fromDate, DateOnly to, CancellationToken ct)
    {
        Dictionary<long, string> suppliers = await SuppliersAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> users = await UsersAsync(ct).ConfigureAwait(false);
        List<PurchaseRequisition> requisitions = await _db.Set<PurchaseRequisition>().AsNoTracking().Include(r => r.Lines)
            .Where(r => r.CompanyId == companyId && r.RequestedDate >= fromDate && r.RequestedDate <= to).OrderBy(r => r.RequestedDate).ThenBy(r => r.RequisitionNumber).ToListAsync(ct).ConfigureAwait(false);
        List<string> numbers = [.. requisitions.Select(r => r.RequisitionNumber)];
        Dictionary<string, DateTimeOffset> approvals = (await _db.Set<WorkflowInstance>().AsNoTracking().Include(w => w.Approvals)
                .Where(w => w.CompanyId == companyId && numbers.Contains(w.DocumentReference)).ToListAsync(ct).ConfigureAwait(false))
            .Where(w => w.Approvals.Any(a => a.Action == WorkflowActionType.Approve)).GroupBy(w => w.DocumentReference)
            .ToDictionary(g => g.Key, g => g.SelectMany(w => w.Approvals).Where(a => a.Action == WorkflowActionType.Approve).Max(a => a.OccurredOnUtc));
        return [.. requisitions.Select(r =>
        {
            DateOnly? approved = approvals.TryGetValue(r.RequisitionNumber, out DateTimeOffset at) ? DateOnly.FromDateTime(at.UtcDateTime)
                : r.ApprovedByUserId is not null && r.ModifiedOnUtc is DateTimeOffset m ? DateOnly.FromDateTime(m.UtcDateTime) : null;
            return Row(("Requisition", r.RequisitionNumber), ("Supplier", suppliers.GetValueOrDefault(r.SupplierId, "?")), ("Requested", Date(r.RequestedDate)), ("Status", r.Status.ToString()),
                ("Approver", r.ApprovedByUserId is long u ? users.GetValueOrDefault(u, $"user {u}") : "—"), ("Approved", approved is DateOnly d ? Date(d) : "—"),
                ("Days", approved is DateOnly a ? Math.Max(0, a.DayNumber - r.RequestedDate.DayNumber).ToString(CultureInfo.InvariantCulture) : "—"),
                ("Value", r.Lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero))));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ApprovalHistoryAsync(long companyId, DateOnly fromDate, DateOnly to, CancellationToken ct)
    {
        DateTimeOffset start = new(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), end = new(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        Dictionary<long, string> users = await UsersAsync(ct).ConfigureAwait(false);
        List<WorkflowInstance> instances = await _db.Set<WorkflowInstance>().AsNoTracking().Include(w => w.Approvals)
            .Where(w => w.CompanyId == companyId && w.Approvals.Any(a => a.OccurredOnUtc >= start && a.OccurredOnUtc <= end)).ToListAsync(ct).ConfigureAwait(false);
        return [.. instances.SelectMany(w => w.Approvals.Where(a => a.OccurredOnUtc >= start && a.OccurredOnUtc <= end).Select(a => (Instance: w, Action: a)))
            .OrderBy(x => x.Action.OccurredOnUtc)
            .Select(x => Row(("When", x.Action.OccurredOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Document", x.Instance.DocumentType), ("Reference", x.Instance.DocumentReference),
                ("Stage", x.Action.StageSequence), ("User", users.GetValueOrDefault(x.Action.UserId, $"user {x.Action.UserId}")), ("Action", x.Action.Action.ToString()),
                ("Comment", x.Action.Comment ?? string.Empty), ("Value", x.Instance.Value)))];
    }

    // How long each stage took: from the instance's start (first stage) or the previous decision to this one; pending stages from when they were entered.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ApprovalSlaAsync(long companyId, DateOnly fromDate, DateOnly to, CancellationToken ct)
    {
        DateTimeOffset start = new(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), end = new(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<WorkflowInstance> instances = await _db.Set<WorkflowInstance>().AsNoTracking().Include(w => w.Approvals)
            .Where(w => w.CompanyId == companyId && (w.Status == WorkflowStatus.InProgress || w.Approvals.Any(a => a.OccurredOnUtc >= start && a.OccurredOnUtc <= end)))
            .ToListAsync(ct).ConfigureAwait(false);
        List<long> definitionIds = [.. instances.Select(w => w.WorkflowDefinitionId).Distinct()];
        Dictionary<(long Definition, int Stage), int?> sla = (await _db.Set<WorkflowDefinition>().AsNoTracking().IgnoreQueryFilters().Include(d => d.Stages)
                .Where(d => definitionIds.Contains(d.Id)).ToListAsync(ct).ConfigureAwait(false))
            .SelectMany(d => d.Stages.Select(s => (Key: (d.Id, s.Sequence), s.EscalateAfterHours))).ToDictionary(x => x.Key, x => x.EscalateAfterHours);

        List<(string Document, int Stage, long Definition, double? Hours, double? WaitingHours)> stages = [];
        foreach (WorkflowInstance instance in instances)
        {
            DateTimeOffset since = instance.CreatedOnUtc;
            foreach (WorkflowApproval decision in instance.Approvals.OrderBy(a => a.OccurredOnUtc))
            {
                if (decision.OccurredOnUtc >= start && decision.OccurredOnUtc <= end)
                {
                    stages.Add((instance.DocumentType, decision.StageSequence, instance.WorkflowDefinitionId, (decision.OccurredOnUtc - since).TotalHours, null));
                }

                since = decision.OccurredOnUtc;
            }

            if (instance.Status == WorkflowStatus.InProgress)
            {
                stages.Add((instance.DocumentType, instance.CurrentStageSequence, instance.WorkflowDefinitionId, null, (now - (instance.StageEnteredOnUtc ?? since)).TotalHours));
            }
        }

        return [.. stages.GroupBy(s => (s.Document, s.Stage)).OrderBy(g => g.Key.Document, StringComparer.Ordinal).ThenBy(g => g.Key.Stage).Select(g =>
        {
            List<double> decided = [.. g.Where(s => s.Hours is not null).Select(s => s.Hours!.Value)];
            List<(double Hours, int? Sla)> timed = [.. g.Where(s => s.Hours is not null).Select(s => (s.Hours!.Value, sla.GetValueOrDefault((s.Definition, s.Stage))))];
            List<double> waiting = [.. g.Where(s => s.WaitingHours is not null).Select(s => s.WaitingHours!.Value)];
            int? stageSla = g.Select(s => sla.GetValueOrDefault((s.Definition, s.Stage))).FirstOrDefault(h => h is not null);
            return Row(("Document", g.Key.Document), ("Stage", g.Key.Stage), ("Decisions", decided.Count),
                ("AverageHours", decided.Count == 0 ? "—" : Math.Round(decided.Average(), 1).ToString("0.0", CultureInfo.InvariantCulture)),
                ("LongestHours", decided.Count == 0 ? "—" : Math.Round(decided.Max(), 1).ToString("0.0", CultureInfo.InvariantCulture)),
                ("SlaHours", stageSla?.ToString(CultureInfo.InvariantCulture) ?? "—"), ("Breaches", timed.Count(t => t.Sla is int h && t.Hours > h)), ("Waiting", waiting.Count),
                ("OldestWaitHours", waiting.Count == 0 ? 0m : (decimal)Math.Round(waiting.Max(), 1)));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> OrdersAsync(long companyId, DateOnly fromDate, DateOnly to, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, string> suppliers = await SuppliersAsync(companyId, ct).ConfigureAwait(false);
        List<PurchaseOrder> orders = await _db.Set<PurchaseOrder>().AsNoTracking().Include(o => o.Lines)
            .Where(o => o.CompanyId == companyId && o.OrderDate >= fromDate && o.OrderDate <= to).OrderBy(o => o.OrderDate).ThenBy(o => o.OrderNumber).ToListAsync(ct).ConfigureAwait(false);
        return [.. orders.Select(o =>
        {
            decimal ordered = o.Lines.Sum(l => Value(l.OrderedQuantity, l.UnitPrice));
            decimal received = o.Lines.Sum(l => Value(l.ReceivedQuantity, l.UnitPrice));
            bool open = !Done.Contains(o.Status);
            return Row(("Order", o.OrderNumber), ("Date", Date(o.OrderDate)), ("Supplier", suppliers.GetValueOrDefault(o.SupplierId, "?")),
                ("Status", o.AwaitingReapproval ? $"{o.Status} (awaiting re-approval)" : o.Status.ToString()), ("RequiredBy", o.RequiredBy is DateOnly r ? Date(r) : "—"),
                ("Overdue", open && o.RequiredBy is DateOnly due && due < asAt ? $"{asAt.DayNumber - due.DayNumber} days" : "—"), ("Currency", o.Currency.Value),
                ("Ordered", Money(ordered)), ("Received", Money(received)), ("Outstanding", open ? ordered - received : 0m));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CommitmentsAsync(long companyId, CancellationToken ct)
        => [.. (await _db.Set<BudgetCommitment>().AsNoTracking().Where(c => c.CompanyId == companyId).OrderBy(c => c.AccountCode).ThenBy(c => c.Department).ThenBy(c => c.Period).ThenBy(c => c.Reference)
                .ToListAsync(ct).ConfigureAwait(false))
            .Select(c => Row(("Account", c.AccountCode), ("Department", c.Department ?? "—"), ("Period", c.Period), ("Reference", c.Reference), ("Type", c.Type.ToString()), ("Status", c.Status.ToString()),
                ("Amount", c.Status == CommitmentStatus.Open ? c.Amount : 0m)))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GoodsReceivedAsync(long companyId, DateOnly fromDate, DateOnly to, CancellationToken ct)
    {
        Dictionary<long, string> suppliers = await SuppliersAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> items = await ItemsAsync(companyId, ct).ConfigureAwait(false);
        List<GoodsReceivedNote> notes = await _db.Set<GoodsReceivedNote>().AsNoTracking().Include(g => g.Lines)
            .Where(g => g.CompanyId == companyId && g.Date >= fromDate && g.Date <= to).OrderBy(g => g.Date).ThenBy(g => g.GrnNumber).ToListAsync(ct).ConfigureAwait(false);
        List<long> orderIds = [.. notes.Select(n => n.PurchaseOrderId).Distinct()];
        Dictionary<long, PurchaseOrder> orders = await _db.Set<PurchaseOrder>().AsNoTracking().Include(o => o.Lines).Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct).ConfigureAwait(false);
        return [.. notes.SelectMany(n => n.Lines.Select(l =>
        {
            PurchaseOrder? order = orders.GetValueOrDefault(n.PurchaseOrderId);
            PurchaseOrderLine? line = order?.Lines.FirstOrDefault(x => x.Id == l.PurchaseOrderLineId);
            decimal priceVariance = line is null ? 0m : Value(l.Quantity, l.UnitPrice - line.UnitPrice);
            return Row(("Grn", n.GrnNumber), ("Date", Date(n.Date)), ("Order", order?.OrderNumber ?? "?"), ("Supplier", suppliers.GetValueOrDefault(n.SupplierId, "?")), ("Item", items.GetValueOrDefault(l.ItemId, "?")),
                ("Ordered", Qty(line?.OrderedQuantity ?? 0m)), ("Received", Qty(line?.ReceivedQuantity ?? l.Quantity)),
                ("QuantityVariance", line is null ? "—" : Qty(line.ReceivedQuantity - line.OrderedQuantity)), ("PriceVariance", Money(priceVariance)), ("Value", Value(l.Quantity, l.UnitPrice)));
        }))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RejectionsAsync(long companyId, DateOnly fromDate, DateOnly to, CancellationToken ct)
    {
        Dictionary<long, string> suppliers = await SuppliersAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> items = await ItemsAsync(companyId, ct).ConfigureAwait(false);
        var inspections = await (from i in _db.Set<GrnInspection>().AsNoTracking().Include(i => i.Lines)
                                 join g in _db.Set<GoodsReceivedNote>().AsNoTracking().Include(g => g.Lines) on i.GoodsReceivedNoteId equals g.Id
                                 where i.CompanyId == companyId && i.InspectedOn >= fromDate && i.InspectedOn <= to
                                 orderby i.InspectedOn, g.GrnNumber
                                 select new { Inspection = i, Note = g }).ToListAsync(ct).ConfigureAwait(false);
        return [.. inspections.SelectMany(x => x.Inspection.Lines.Where(l => l.Rejected > 0m).Select(l =>
        {
            decimal price = x.Note.Lines.FirstOrDefault(g => g.ItemId == l.ItemId)?.UnitPrice ?? 0m;
            return Row(("Grn", x.Note.GrnNumber), ("Inspected", Date(x.Inspection.InspectedOn)), ("Supplier", suppliers.GetValueOrDefault(x.Note.SupplierId, "?")), ("Item", items.GetValueOrDefault(l.ItemId, "?")),
                ("Passed", Qty(l.Passed)), ("Rejected", Qty(l.Rejected)), ("Reason", l.RejectionReason ?? "—"), ("Value", Value(l.Rejected, price)));
        }))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RfqsAsync(long companyId, CancellationToken ct)
    {
        Dictionary<long, string> suppliers = await SuppliersAsync(companyId, ct).ConfigureAwait(false);
        List<Rfq> rfqs = await _db.Set<Rfq>().AsNoTracking().Include(r => r.Lines).Include(r => r.Quotes).ThenInclude(q => q.Prices)
            .Where(r => r.CompanyId == companyId).OrderBy(r => r.IssuedOn).ThenBy(r => r.RfqNumber).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (Rfq rfq in rfqs)
        {
            var quotes = rfq.Quotes.Select(q => (Quote: q, Total: rfq.Lines.Sum(l => Value(l.Quantity, q.Prices.FirstOrDefault(p => p.ItemId == l.ItemId)?.UnitPrice ?? 0m))))
                .OrderBy(x => x.Total).ThenBy(x => x.Quote.LeadTimeDays).ToList();
            int rank = 0;
            rows.AddRange(quotes.Select(x => Row(("Rfq", rfq.RfqNumber), ("Supplier", suppliers.GetValueOrDefault(x.Quote.SupplierId, "?")), ("Received", Date(x.Quote.ReceivedOn)),
                ("LeadDays", x.Quote.LeadTimeDays), ("Rank", ++rank), ("Awarded", rfq.AwardedSupplierId == x.Quote.SupplierId ? "Yes" : string.Empty),
                ("Reason", rfq.AwardedSupplierId == x.Quote.SupplierId ? rfq.AwardReason ?? string.Empty : string.Empty), ("Total", x.Total))));
        }

        return rows;
    }

    // The previous price of an item is its price on the latest earlier order line (any supplier).
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PriceVarianceAsync(long companyId, DateOnly fromDate, DateOnly to, CancellationToken ct)
    {
        Dictionary<long, string> suppliers = await SuppliersAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> items = await ItemsAsync(companyId, ct).ConfigureAwait(false);
        var lines = (await _db.Set<PurchaseOrder>().AsNoTracking().Include(o => o.Lines).Where(o => o.CompanyId == companyId && o.Status != PurchaseOrderStatus.Cancelled && o.OrderDate <= to)
                .ToListAsync(ct).ConfigureAwait(false))
            .SelectMany(o => o.Lines.Select(l => (Order: o, Line: l))).OrderBy(x => x.Order.OrderDate).ThenBy(x => x.Order.Id).ThenBy(x => x.Line.Id).ToList();
        Dictionary<long, decimal> lastPrice = [];
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach ((PurchaseOrder order, PurchaseOrderLine line) in lines)
        {
            if (order.OrderDate >= fromDate && lastPrice.TryGetValue(line.ItemId, out decimal previous) && previous != line.UnitPrice)
            {
                rows.Add(Row(("Item", items.GetValueOrDefault(line.ItemId, "?")), ("Order", order.OrderNumber), ("Date", Date(order.OrderDate)), ("Supplier", suppliers.GetValueOrDefault(order.SupplierId, "?")),
                    ("Previous", Money(previous)), ("Price", Money(line.UnitPrice)),
                    ("Change", previous == 0m ? "—" : ((line.UnitPrice - previous) * 100m / previous).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%"),
                    ("Quantity", Qty(line.OrderedQuantity)), ("Variance", Value(line.OrderedQuantity, line.UnitPrice - previous))));
            }

            lastPrice[line.ItemId] = line.UnitPrice;
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SpendAsync(long companyId, DateOnly fromDate, DateOnly to, CancellationToken ct)
    {
        Dictionary<long, string> suppliers = await SuppliersAsync(companyId, ct).ConfigureAwait(false);
        List<PurchaseOrder> orders = await _db.Set<PurchaseOrder>().AsNoTracking().Include(o => o.Lines)
            .Where(o => o.CompanyId == companyId && o.Status != PurchaseOrderStatus.Cancelled && o.OrderDate >= fromDate && o.OrderDate <= to).ToListAsync(ct).ConfigureAwait(false);
        var groups = orders.GroupBy(o => (o.SupplierId, Currency: o.Currency.Value))
            .Select(g => (g.Key, Orders: g.Count(), Lines: g.Sum(o => o.Lines.Count), Items: g.SelectMany(o => o.Lines).Select(l => l.ItemId).Distinct().Count(),
                Spend: g.SelectMany(o => o.Lines).Sum(l => Value(l.OrderedQuantity, l.UnitPrice)))).ToList();
        Dictionary<string, decimal> totals = groups.GroupBy(g => g.Key.Currency).ToDictionary(g => g.Key, g => g.Sum(x => x.Spend));
        return [.. groups.OrderBy(g => g.Key.Currency, StringComparer.Ordinal).ThenByDescending(g => g.Spend)
            .Select(g => Row(("Supplier", suppliers.GetValueOrDefault(g.Key.SupplierId, "?")), ("Currency", g.Key.Currency), ("Orders", g.Orders), ("Lines", g.Lines), ("Items", g.Items),
                ("Share", totals[g.Key.Currency] == 0m ? "—" : (g.Spend * 100m / totals[g.Key.Currency]).ToString("0.0", CultureInfo.InvariantCulture) + "%"), ("Spend", g.Spend)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LeadTimesAsync(long companyId, DateOnly fromDate, DateOnly to, CancellationToken ct)
    {
        Dictionary<long, string> suppliers = await SuppliersAsync(companyId, ct).ConfigureAwait(false);
        var received = await (from o in _db.Set<PurchaseOrder>().AsNoTracking()
                              join g in _db.Set<GoodsReceivedNote>().AsNoTracking() on o.Id equals g.PurchaseOrderId
                              where o.CompanyId == companyId && o.OrderDate >= fromDate && o.OrderDate <= to
                              group g by new { o.Id, o.SupplierId, o.OrderDate, o.RequiredBy } into r
                              select new { r.Key.SupplierId, r.Key.OrderDate, r.Key.RequiredBy, First = r.Min(x => x.Date) }).ToListAsync(ct).ConfigureAwait(false);
        return [.. received.GroupBy(r => r.SupplierId).OrderBy(g => suppliers.GetValueOrDefault(g.Key), StringComparer.Ordinal).Select(g =>
        {
            List<int> days = [.. g.Select(r => r.First.DayNumber - r.OrderDate.DayNumber)];
            int withDate = g.Count(r => r.RequiredBy is not null);
            int onTime = g.Count(r => r.RequiredBy is DateOnly due && r.First <= due);
            return Row(("Supplier", suppliers.GetValueOrDefault(g.Key, "?")), ("Orders", g.Count()), ("Average", days.Average().ToString("0.0", CultureInfo.InvariantCulture)), ("Fastest", days.Min()),
                ("Slowest", days.Max()), ("OnTime", withDate == 0 ? "—" : $"{onTime} of {withDate}"));
        })];
    }

    private Task<Dictionary<long, string>> SuppliersAsync(long companyId, CancellationToken ct)
        => _db.Suppliers.AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(s => s.CompanyId == companyId).ToDictionaryAsync(s => s.Id, s => s.Name, ct);

    private Task<Dictionary<long, string>> ItemsAsync(long companyId, CancellationToken ct)
        => _db.Set<Item>().AsNoTracking().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(i => i.CompanyId == companyId).ToDictionaryAsync(i => i.Id, i => i.Code + " " + i.Description, ct);

    private Task<Dictionary<long, string>> UsersAsync(CancellationToken ct) => _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username, ct);

    private static decimal Value(decimal quantity, decimal price) => Math.Round(quantity * price, 2, MidpointRounding.AwayFromZero);

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
