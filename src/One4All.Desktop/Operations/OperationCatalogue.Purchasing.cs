using System.Data;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Procurement;
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
            Module = module, Title = "Schedule a PO line's deliveries", ListTitle = "Deliveries late or due within a week",
            Description = "Spreads an order line over due dates (DATE:qty;… as yyyy-MM-dd:qty). The quantities must add up to the line's ordered quantity.",
            Fields = [Req("Order", "Purchase order number"), Req("Item", "Item code"), Req("Deliveries", "Deliveries yyyy-MM-dd:qty;…")],
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
