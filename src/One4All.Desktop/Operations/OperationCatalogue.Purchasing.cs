using System.Data;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Budgeting;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Purchasing operations: delivery schedules and expediting.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Purchasing()
    {
        const string module = "Purchasing";
        yield return new Operation
        {
            Module = module, Title = "Set a budget enforcement rule", ListTitle = "Budget enforcement rules",
            Description = "What happens when spend overruns the available budget, by department, account prefix and value band (the most specific rule wins; none: allow).",
            Fields = [Opt("Department", "Department (blank: any)"), Opt("Prefix", "Account prefix (blank: any)"), Num("From", "From value", "0"), Pick("Action", "Action", Enum.GetNames<BudgetAction>())],
            Run = async (c, i) => Check(await c.Get<IBudgetGateService>().SetRuleAsync(c.CompanyId, i.Optional("Department"), i.Optional("Prefix"), i.Number("From"),
                Enum.Parse<BudgetAction>(i.Text("Action"))).ConfigureAwait(true), "Rule saved."),
            List = async c => Table(await c.Db.BudgetEnforcementRules.AsNoTracking().Where(r => r.CompanyId == c.CompanyId).OrderBy(r => r.Department).ThenBy(r => r.AccountPrefix).ThenBy(r => r.MinimumValue)
                .ToListAsync().ConfigureAwait(true), ("Department", r => r.Department ?? "(any)"), ("Account prefix", r => r.AccountPrefix ?? "(any)"), ("From value", r => M(r.MinimumValue)), ("Action", r => r.Action)),
        };
        yield return new Operation
        {
            Module = module, Title = "Check or reserve budget", ListTitle = "Open budget commitments",
            Description = "Shows what is available on an account; tick Reserve to earmark it for known spend with no order yet (released by its reference).",
            Fields = [Req("Account", "Expense account"), Opt("Department", "Department"), Num("Amount", "Amount"), Today(), Tick("Reserve", "Reserve it"), Opt("Reference", "Reservation reference")],
            Run = async (c, i) =>
            {
                BudgetDemand demand = new(i.Text("Account"), i.Optional("Department"), i.Number("Amount"));
                IBudgetGateService gate = c.Get<IBudgetGateService>();
                BudgetGateResult result = i.Flag("Reserve")
                    ? Check(await gate.ReserveAsync(c.CompanyId, i.Optional("Reference") ?? throw new OperationException("A reservation needs a reference."), i.Date("Date"), demand).ConfigureAwait(true))
                    : Check(await gate.CheckAsync(c.CompanyId, i.Date("Date"), [demand]).ConfigureAwait(true));
                BudgetGateLine line = result.Lines.Single();
                return $"Available {M(line.Available)}; {(line.Shortfall > 0m ? $"short by {M(line.Shortfall)} ({line.Action})" : "fits")}{(i.Flag("Reserve") ? "; reserved" : string.Empty)}.";
            },
            List = async c => Table(await c.Db.Set<BudgetCommitment>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.Status == CommitmentStatus.Open).OrderBy(x => x.Reference)
                .ToListAsync().ConfigureAwait(true), ("Reference", x => x.Reference), ("Account", x => x.AccountCode), ("Department", x => x.Department), ("Period", x => x.Period), ("Amount", x => M(x.Amount))),
        };
        yield return new Operation
        {
            Module = module, Title = "Reconcile budget commitments", ListTitle = "Commitment exceptions",
            Description = "Compares the commitment ledger with purchase orders: stale commitments, orders with none, and amounts that differ. Release a stale one by its reference.",
            Fields = [Opt("Release", "Reference to release (optional)")],
            Run = async (c, i) =>
            {
                IBudgetGateService gate = c.Get<IBudgetGateService>();
                string released = i.Optional("Release") is string reference ? $"Released {M(await gate.ReleaseAsync(c.CompanyId, reference).ConfigureAwait(true))} under {reference}. " : string.Empty;
                return $"{released}{(await gate.ReconcileAsync(c.CompanyId).ConfigureAwait(true)).Count} exception(s).";
            },
            List = async c => Table(await c.Get<IBudgetGateService>().ReconcileAsync(c.CompanyId).ConfigureAwait(true),
                ("Reference", e => e.Reference), ("Account", e => e.AccountCode), ("Committed", e => M(e.Committed)), ("On order", e => M(e.OnOrder)), ("Problem", e => e.Problem)),
        };
        yield return new Operation
        {
            Module = module, Title = "Schedule a PO line's deliveries", ListTitle = "Deliveries late or due within a week",
            Description = "Spreads an order line over due dates (DATE:qty;… as yyyy-MM-dd:qty). The quantities must add up to the line's ordered quantity.",
            Fields = [Req("Order", "Purchase order number"), ItemPick("Item", "Product"), Req("Deliveries", "Deliveries yyyy-MM-dd:qty;…")],
            Run = async (c, i) =>
            {
                string order = i.Text("Order").Trim().ToUpperInvariant();
                long item = await ItemAsync(c, i.Text("Item")).ConfigureAwait(true);
                long line = await FindAsync(c.Db.Set<Core.Procurement.PurchaseOrderLine>(), l => l.ItemId == item && c.Db.PurchaseOrders.Any(o => o.Id == l.PurchaseOrderId && o.CompanyId == c.CompanyId && o.OrderNumber.ToUpper() == order),
                    l => l.Id, "order line", $"{order} {i.Text("Item")}").ConfigureAwait(true);
                List<(DateOnly Due, decimal Quantity)> deliveries = [];
                foreach (var (date, quantity, _) in i.Pairs("Deliveries"))
                {
                    deliveries.Add((DateOnly.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), quantity));
                }

                return Check(await c.Get<IDeliveryScheduleService>().ScheduleAsync(c.CompanyId, line, deliveries).ConfigureAwait(true), $"{deliveries.Count} deliveries scheduled.");
            },
            List = ExpediteAsync,
        };
    }

    private static async Task<DataTable> ExpediteAsync(OperationContext c)
        => Table(await c.Get<IDeliveryScheduleService>().ExpediteAsync(c.CompanyId, DateOnly.FromDateTime(DateTime.Today), daysAhead: 7).ConfigureAwait(true),
            ("Order", e => e.OrderNumber), ("Supplier", e => e.SupplierCode), ("Item", e => e.ItemCode), ("Due", e => D(e.DueDate)), ("Scheduled", e => Q(e.Scheduled)), ("Received", e => Q(e.Received)),
            ("Outstanding", e => Q(e.Outstanding)), ("Days late", e => e.DaysLate));
}
