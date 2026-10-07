using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Accounting;
using One4All.Core.Procurement;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Purchase-to-pay: requisitions, purchase orders, goods received and the supplier invoice matched to the order.</content>
public static partial class OperationCatalogue
{
    private const string LinesHelp = "Lines: ITEM:quantity@price; ITEM:quantity@price";

    private static IEnumerable<Operation> PurchaseOrders()
    {
        const string module = "Purchasing";
        yield return new Operation
        {
            Module = module, Title = "Purchase requisition", ListTitle = "Requisitions",
            Description = "Raise a requisition (budget-checked), submit it, approve or reject it (someone else approves), and convert an approved one into a purchase order. " + LinesHelp,
            Fields = [Pick("Step", "Step", ["Create", "Submit", "Approve", "Reject", "Convert to order"]), Opt("Number", "Requisition number"), Opt("Supplier", "Supplier code (create)"),
                Opt("Currency", "Currency", "USD"), Opt("Lines", "Lines (create)"), Opt("Order", "New order number (convert)"), Today("Date", "Date")],
            Run = async (c, i) =>
            {
                IPurchaseRequisitionService requisitions = c.Get<IPurchaseRequisitionService>();
                string number = i.Text("Number").Trim();
                if (i.Text("Step") == "Create")
                {
                    List<PurchaseRequisitionLineRequest> lines = [];
                    foreach ((long item, string description, decimal quantity, decimal price) in await OrderLinesAsync(c, i.Text("Lines")).ConfigureAwait(true))
                    {
                        lines.Add(new PurchaseRequisitionLineRequest(item, description, quantity, price));
                    }

                    long id = Check(await requisitions.CreateAsync(c.CompanyId, await SupplierAsync(c, i.Text("Supplier")).ConfigureAwait(true), number, i.Date("Date"),
                        CurrencyCode.Parse(i.Optional("Currency") ?? "USD"), lines).ConfigureAwait(true));
                    return $"Requisition {number} created (id {id}); submit it for approval.";
                }

                long requisition = await c.Db.PurchaseRequisitions.AsNoTracking().Where(r => r.CompanyId == c.CompanyId && r.RequisitionNumber == number).Select(r => (long?)r.Id).FirstOrDefaultAsync()
                    .ConfigureAwait(true) ?? throw new OperationException($"No requisition {number}.");
                switch (i.Text("Step"))
                {
                    case "Submit":
                        _ = Check(await requisitions.SubmitAsync(c.CompanyId, requisition, c.UserId).ConfigureAwait(true), string.Empty);
                        return "Submitted for approval.";
                    case "Approve":
                        _ = Check(await requisitions.ApproveAsync(c.CompanyId, requisition, c.UserId).ConfigureAwait(true), string.Empty);
                        return "Approved.";
                    case "Reject":
                        _ = Check(await requisitions.RejectAsync(c.CompanyId, requisition).ConfigureAwait(true), string.Empty);
                        return "Rejected.";
                    default:
                        string order = i.Optional("Order") ?? throw new OperationException("Give the new order number.");
                        _ = Check(await requisitions.ConvertToPurchaseOrderAsync(c.CompanyId, requisition, order, i.Date("Date")).ConfigureAwait(true));
                        return $"Purchase order {order} raised from the requisition.";
                }
            },
            List = async c => Table(await c.Db.PurchaseRequisitions.AsNoTracking().Include(r => r.Lines).Where(r => r.CompanyId == c.CompanyId).OrderByDescending(r => r.Id).Take(200).ToListAsync()
                    .ConfigureAwait(true), ("Number", r => r.RequisitionNumber), ("Supplier", r => r.SupplierId), ("Date", r => D(r.RequestedDate)), ("Currency", r => r.Currency),
                ("Value", r => M(r.Lines.Sum(l => l.Quantity * l.UnitPrice))), ("Status", r => r.Status), ("Budget", r => r.BudgetOutcome), ("Order", r => r.PurchaseOrderId)),
        };
        yield return new Operation
        {
            Module = module, Title = "Purchase order", ListTitle = "Purchase orders",
            Description = "Raise an order directly (budget-checked; over-budget needs an approver), cancel one, or print it. " + LinesHelp,
            Fields = [Pick("Step", "Step", ["Create", "Print", "Cancel"]), Req("Number", "Order number"), Opt("Supplier", "Supplier code (create)"), Opt("Currency", "Currency", "USD"),
                Opt("Lines", "Lines (create)"), Opt("RequiredBy", "Required by (yyyy-MM-dd)"), Opt("BudgetApprover", "Budget approver user (over budget)"), Opt("Reason", "Reason (cancel)"), Today("Date", "Order date"),
                Opt("Job", "For job (contract number): received straight to the job"), Opt("JobAccount", "Job WIP account"), Opt("JobHead", "Job cost head", "Materials")],
            Run = async (c, i) =>
            {
                IProcurementService procurement = c.Get<IProcurementService>();
                if (i.Text("Step") == "Create")
                {
                    long? job = i.Optional("Job") is string jobNumber
                        ? await FindAsync(c.Db.JobContracts, j => j.CompanyId == c.CompanyId && j.ContractNumber.ToUpper() == jobNumber.ToUpper(), j => j.Id, "contract", jobNumber).ConfigureAwait(true)
                        : null;
                    List<PurchaseOrderLineRequest> lines = [];
                    foreach ((long item, string description, decimal quantity, decimal price) in await OrderLinesAsync(c, i.Text("Lines")).ConfigureAwait(true))
                    {
                        lines.Add(new PurchaseOrderLineRequest(item, description, quantity, price, job is null ? null : i.Optional("JobAccount"), job, i.Optional("JobHead")));
                    }

                    PurchaseOrderResult created = Check(await procurement.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
                    {
                        CompanyId = c.CompanyId, SupplierId = await SupplierAsync(c, i.Text("Supplier")).ConfigureAwait(true), OrderNumber = i.Text("Number"), OrderDate = i.Date("Date"),
                        RequiredBy = i.Optional("RequiredBy") is string by ? Day(by) : null, Currency = CurrencyCode.Parse(i.Optional("Currency") ?? "USD"), Lines = lines,
                        BudgetApprovedByUserId = await OptionalUserAsync(c, i.Optional("BudgetApprover")).ConfigureAwait(true),
                    }).ConfigureAwait(true));
                    return $"Purchase order {created.OrderNumber} raised.";
                }

                long order = OrderId(c, i.Text("Number"));
                if (i.Text("Step") == "Print")
                {
                    return Check(await c.Get<IPurchaseOrderDocumentService>().RenderTextAsync(c.CompanyId, order).ConfigureAwait(true));
                }

                _ = Check(await procurement.CancelPurchaseOrderAsync(c.CompanyId, order, i.Optional("Reason") ?? throw new OperationException("Give the reason.")).ConfigureAwait(true), string.Empty);
                return "Order cancelled.";
            },
            List = PurchaseOrdersAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Receive goods", ListTitle = "Purchase orders",
            Description = "Books a goods received note against an order into a warehouse (stock in, goods-received-not-invoiced accrued). Leave Lines blank to receive everything still outstanding; "
                + "otherwise ITEM:quantity; ITEM:quantity.",
            Fields = [Req("Order", "Order number"), Req("Warehouse", "Warehouse code"), Req("Grn", "GRN number"), Opt("Lines", "Lines (blank: all outstanding)"), Today("Date", "Date"),
                Opt("Supervisor", "Supervisor authorising an over-receipt (user name)")],
            Run = async (c, i) =>
            {
                long orderId = OrderId(c, i.Text("Order"));
                PurchaseOrder order = await c.Db.PurchaseOrders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == orderId).ConfigureAwait(true);
                List<ReceiveLine> lines = i.Optional("Lines") is string text
                    ? [.. (await QuantitiesAsync(c, text).ConfigureAwait(true)).Select(q => new ReceiveLine(LineFor(order, q.Item).Id, q.Quantity))]
                    : [.. order.Lines.Where(l => l.OrderedQuantity > l.ReceivedQuantity).Select(l => new ReceiveLine(l.Id, l.OrderedQuantity - l.ReceivedQuantity))];
                GrnResult grn = Check(await c.Get<IProcurementService>().ReceiveGoodsAsync(new ReceiveGoodsRequest
                {
                    CompanyId = c.CompanyId, PurchaseOrderId = order.Id, WarehouseId = await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true), GrnNumber = i.Text("Grn"),
                    Date = i.Date("Date"), Lines = lines, ReceivedByUserId = c.UserId,
                    OverReceiptAuthorisedByUserId = await OptionalUserAsync(c, i.Optional("Supervisor")).ConfigureAwait(true),
                }).ConfigureAwait(true));
                return $"GRN {i.Text("Grn")} booked: {lines.Count} line(s), {M(grn.AccruedValue)} accrued.";
            },
            List = PurchaseOrdersAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Match a supplier invoice", ListTitle = "Purchase orders",
            Description = "Posts the supplier's invoice against the order (three-way match within the price tolerance; a larger difference is held as a dispute). Leave Lines blank to invoice "
                + "everything received and not yet invoiced at the order price; otherwise ITEM:quantity@price.",
            Fields = [Req("Order", "Order number"), Req("Invoice", "Supplier invoice number"), Opt("Lines", "Lines (blank: received, at order price)"), Num("Tolerance", "Price tolerance %", "5"),
                Opt("Variance", "Price variance account"), Opt("BudgetApprover", "Budget approver user (over budget)"), Today("Date", "Invoice date")],
            Run = async (c, i) =>
            {
                long orderId = OrderId(c, i.Text("Order"));
                PurchaseOrder order = await c.Db.PurchaseOrders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == orderId).ConfigureAwait(true);
                List<InvoiceMatchLine> lines = i.Optional("Lines") is string text
                    ? [.. (await OrderLinesAsync(c, text).ConfigureAwait(true)).Select(l => new InvoiceMatchLine(LineFor(order, l.Item).Id, l.Quantity, l.Price))]
                    : [.. order.Lines.Where(l => l.ReceivedQuantity > l.InvoicedQuantity).Select(l => new InvoiceMatchLine(l.Id, l.ReceivedQuantity - l.InvoicedQuantity, l.UnitPrice))];
                MatchInvoiceResult matched = Check(await c.Get<IProcurementService>().MatchInvoiceAsync(new MatchInvoiceRequest
                {
                    CompanyId = c.CompanyId, PurchaseOrderId = order.Id, InvoiceNumber = i.Text("Invoice"), Date = i.Date("Date"), TolerancePercent = i.Number("Tolerance"),
                    PriceVarianceAccountCode = i.Optional("Variance"), BudgetApprovedByUserId = await OptionalUserAsync(c, i.Optional("BudgetApprover")).ConfigureAwait(true), Lines = lines,
                }).ConfigureAwait(true));
                return $"Invoice {i.Text("Invoice")} posted: {M(matched.InvoiceValue)} (journal {matched.JournalNumber}).";
            },
            List = PurchaseOrdersAsync,
        };
    }

    private static async Task<System.Data.DataTable> PurchaseOrdersAsync(OperationContext c)
        => Table(await c.Db.PurchaseOrders.AsNoTracking().Include(o => o.Lines).Where(o => o.CompanyId == c.CompanyId).OrderByDescending(o => o.Id).Take(200).ToListAsync().ConfigureAwait(true),
            ("Order", o => o.OrderNumber), ("Supplier", o => o.SupplierId), ("Date", o => D(o.OrderDate)), ("Currency", o => o.Currency), ("Value", o => M(o.Lines.Sum(l => l.OrderedQuantity * l.UnitPrice))),
            ("Received", o => $"{Q(o.Lines.Sum(l => l.ReceivedQuantity))}/{Q(o.Lines.Sum(l => l.OrderedQuantity))}"), ("Invoiced", o => Q(o.Lines.Sum(l => l.InvoicedQuantity))), ("Status", o => o.Status),
            ("Version", o => o.Version));

    private static long OrderId(OperationContext c, string number)
    {
        string key = number.Trim();
        return c.Db.PurchaseOrders.AsNoTracking().Where(o => o.CompanyId == c.CompanyId && o.OrderNumber == key).Select(o => (long?)o.Id).FirstOrDefault() ?? throw new OperationException($"No order {key}.");
    }

    private static PurchaseOrderLine LineFor(PurchaseOrder order, long itemId)
        => order.Lines.FirstOrDefault(l => l.ItemId == itemId) ?? throw new OperationException($"Order {order.OrderNumber} has no line for item {itemId}.");

    // "ITEM:qty@price; ITEM:qty@price"
    private static async Task<List<(long Item, string Description, decimal Quantity, decimal Price)>> OrderLinesAsync(OperationContext c, string text)
    {
        List<(long, string, decimal, decimal)> lines = [];
        foreach (string part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] codeRest = part.Split(':', 2, StringSplitOptions.TrimEntries);
            string[] qtyPrice = codeRest.Length == 2 ? codeRest[1].Split('@', StringSplitOptions.TrimEntries) : [];
            if (qtyPrice.Length != 2 || !decimal.TryParse(qtyPrice[0], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal quantity)
                || !decimal.TryParse(qtyPrice[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal price))
            {
                throw new OperationException($"\"{part}\" should be ITEM:quantity@price.");
            }

            long item = await ItemAsync(c, codeRest[0]).ConfigureAwait(true);
            string description = await c.Db.Items.AsNoTracking().Where(x => x.Id == item).Select(x => x.Description).SingleAsync().ConfigureAwait(true);
            lines.Add((item, description, quantity, price));
        }

        return lines.Count > 0 ? lines : throw new OperationException("Give at least one line.");
    }

    // "ITEM:qty; ITEM:qty"
    private static async Task<List<(long Item, decimal Quantity)>> QuantitiesAsync(OperationContext c, string text)
    {
        List<(long, decimal)> lines = [];
        foreach (string part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
            if (bits.Length != 2 || !decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal quantity))
            {
                throw new OperationException($"\"{part}\" should be ITEM:quantity.");
            }

            lines.Add((await ItemAsync(c, bits[0]).ConfigureAwait(true), quantity));
        }

        return lines;
    }
}
