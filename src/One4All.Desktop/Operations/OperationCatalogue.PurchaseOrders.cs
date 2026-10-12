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
                        BudgetApprovedByUserId = await OptionalUserAsync(c, i.Optional("BudgetApprover")).ConfigureAwait(true), RaisedByUserId = c.UserId,
                    }).ConfigureAwait(true));
                    bool held = await c.Db.PurchaseOrders.AsNoTracking().AnyAsync(o => o.Id == created.PurchaseOrderId && o.AwaitingApproval).ConfigureAwait(true);
                    return held ? $"Purchase order {created.OrderNumber} raised; it is awaiting approval before goods can be received." : $"Purchase order {created.OrderNumber} raised.";
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
        yield return new Operation
        {
            Module = module, Title = "Requests for quotation", ListTitle = "RFQs",
            Description = "Issue an RFQ to several suppliers (codes comma-separated) for items (ITEM:quantity; …), print it or email it to the suppliers' order contacts, record "
                + "each supplier's quote (ITEM@price; …, lead time), compare them (price and lead time weighted, ranked) and award the RFQ to a supplier, which raises the purchase order.",
            Fields = [Pick("Step", "Step", ["Issue", "Print", "Email", "Quote", "Compare", "Award"]), Req("Rfq", "RFQ number"), Opt("Suppliers", "Supplier codes (issue) / supplier (quote, award)"),
                Opt("Lines", "Items: ITEM:quantity; … (issue) or ITEM@price; … (quote)"), Num("LeadTime", "Lead time (days, quote)", "0"), Opt("Terms", "Terms / award reason"), Num("Weight", "Price weight (compare)", "0.7"),
                Today("Closes", "Closes on (issue)"), Today()],
            Run = async (c, i) =>
            {
                IRfqService rfqs = c.Get<IRfqService>();
                async Task<long> RfqId() => await FindAsync(c.Db.Set<Rfq>(), r => r.CompanyId == c.CompanyId && r.RfqNumber.ToUpper() == i.Text("Rfq").ToUpper(), r => r.Id, "RFQ", i.Text("Rfq")).ConfigureAwait(true);
                switch (i.Text("Step"))
                {
                    case "Issue":
                        List<long> suppliers = [];
                        foreach (string code in (i.Optional("Suppliers") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            suppliers.Add(await SupplierAsync(c, code).ConfigureAwait(true));
                        }

                        List<RfqLineRequest> items = [];
                        foreach ((long item, decimal quantity) in await QuantitiesAsync(c, i.Optional("Lines") ?? string.Empty).ConfigureAwait(true))
                        {
                            string description = await c.Db.Items.AsNoTracking().Where(x => x.Id == item).Select(x => x.Description).FirstAsync().ConfigureAwait(true);
                            items.Add(new RfqLineRequest(item, description, quantity));
                        }

                        return Check(await rfqs.IssueAsync(c.CompanyId, new RfqRequest(i.Text("Rfq"), i.Date("Date"), i.Date("Closes"), suppliers, items)).ConfigureAwait(true), _ => "RFQ issued.");
                    case "Print":
                        return Check(await rfqs.RenderAsync(c.CompanyId, await RfqId().ConfigureAwait(true)).ConfigureAwait(true));
                    case "Email":
                        RfqDespatch sent = Check(await rfqs.SendAsync(c.CompanyId, await RfqId().ConfigureAwait(true)).ConfigureAwait(true));
                        return (sent.BatchId is long batch ? $"Queued to {sent.Recipients} address(es) (batch {batch})." : "Nobody to email.")
                            + (sent.NoEmail.Count == 0 ? string.Empty : $" No email address for: {string.Join(", ", sent.NoEmail)}.");
                    case "Quote":
                        Dictionary<long, decimal> prices = [];
                        foreach (string part in (i.Optional("Lines") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            string[] bits = part.Split('@', StringSplitOptions.TrimEntries);
                            prices[await ItemAsync(c, bits[0]).ConfigureAwait(true)] = bits.Length == 2 && decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal price)
                                ? price : throw new OperationException($"Write each price as ITEM@price (\"{part}\").");
                        }

                        return Check(await rfqs.RecordQuoteAsync(c.CompanyId, await RfqId().ConfigureAwait(true), new QuoteRequest(await SupplierAsync(c, i.Optional("Suppliers") ?? string.Empty).ConfigureAwait(true),
                            i.Date("Date"), i.Whole("LeadTime"), prices, i.Optional("Terms"))).ConfigureAwait(true), "Quote recorded.");
                    case "Compare":
                        RfqComparison comparison = Check(await rfqs.CompareAsync(c.CompanyId, await RfqId().ConfigureAwait(true), i.Number("Weight")).ConfigureAwait(true));
                        return string.Join(Environment.NewLine, comparison.Quotes.OrderBy(q => q.Score.Rank).Select(q =>
                            $"#{q.Score.Rank} {q.SupplierCode,-10} score {q.Score.Total:0.##} (price {q.Score.PriceScore:0.##}, lead time {q.Score.LeadTimeScore:0.##} for {q.Score.LeadTimeDays} days)"))
                            + (comparison.NotQuoted.Count == 0 ? string.Empty : Environment.NewLine + $"Not quoted: {comparison.NotQuoted.Count} supplier(s).");
                    default:
                        PurchaseOrderResult order = Check(await rfqs.AwardAsync(c.CompanyId, await RfqId().ConfigureAwait(true), await SupplierAsync(c, i.Optional("Suppliers") ?? string.Empty).ConfigureAwait(true),
                            i.Date("Date"), c.UserId, i.Optional("Terms")).ConfigureAwait(true));
                        return $"Awarded; purchase order {order.OrderNumber} raised.";
                }
            },
            List = async c => Table(await c.Db.Set<Rfq>().AsNoTracking().Where(r => r.CompanyId == c.CompanyId).OrderByDescending(r => r.Id).Take(100).ToListAsync().ConfigureAwait(true),
                ("RFQ", r => r.RfqNumber)),
        };
        yield return new Operation
        {
            Module = module, Title = "Quality inspection of a receipt",
            Description = "Inspects goods received on a GRN held for quality: per item, what passed (released to the warehouse) and what was rejected (returned, with a credit note) "
                + "— \"ITEM:passed:rejected:reason; …\".",
            Fields = [Req("Grn", "GRN number"), Req("Results", "Results: ITEM:passed:rejected[:reason]; …"), Req("Warehouse", "Release to warehouse"), Opt("Variance", "Price variance account"), Today()],
            Run = async (c, i) =>
            {
                long grn = await FindAsync(c.Db.Set<GoodsReceivedNote>(), g => g.CompanyId == c.CompanyId && g.GrnNumber.ToUpper() == i.Text("Grn").ToUpper(), g => g.Id, "GRN", i.Text("Grn")).ConfigureAwait(true);
                List<InspectionResult> results = [];
                foreach (string part in i.Text("Results").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                    results.Add(bits.Length >= 3 && decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal passed)
                        && decimal.TryParse(bits[2], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal rejected)
                        ? new InspectionResult(await ItemAsync(c, bits[0]).ConfigureAwait(true), passed, rejected, bits.Length > 3 ? bits[3] : null)
                        : throw new OperationException($"Write each result as ITEM:passed:rejected[:reason] (\"{part}\")."));
                }

                InspectionOutcome outcome = Check(await c.Get<IQualityInspectionService>().InspectAsync(c.CompanyId, grn, new InspectionRequest(i.Date("Date"), c.UserId,
                    await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true), i.Optional("Variance") ?? string.Empty, results)).ConfigureAwait(true));
                return $"Released {Q(outcome.Released)}, returned {Q(outcome.Returned)}{(outcome.CreditNoteValue > 0m ? $"; credit note {M(outcome.CreditNoteValue)}" : string.Empty)}.";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Supplier invoice disputes", ListTitle = "Open disputes",
            Description = "Submits a supplier invoice that may not match (outside the tolerance it is held as a dispute rather than refused), then accepts a dispute (posting it, the "
                + "difference to price variance) or rejects it with a note.",
            Fields = [Pick("Step", "Step", ["Submit", "Accept", "Reject"]), Opt("Order", "Order number (submit)"), Opt("Invoice", "Supplier invoice number (submit)"),
                Opt("Lines", "Lines: ITEM:quantity@price; … (submit; blank: received at order price)"), Num("Tolerance", "Price tolerance %", "5"), OptNum("Dispute", "Dispute id"), Opt("Note", "Note"),
                Opt("Variance", "Price variance account"), Today()],
            Run = async (c, i) =>
            {
                IInvoiceDisputeService disputes = c.Get<IInvoiceDisputeService>();
                switch (i.Text("Step"))
                {
                    case "Submit":
                        long orderId = OrderId(c, i.Optional("Order") ?? string.Empty);
                        PurchaseOrder order = await c.Db.PurchaseOrders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == orderId).ConfigureAwait(true);
                        List<InvoiceMatchLine> lines = i.Optional("Lines") is string text
                            ? [.. (await OrderLinesAsync(c, text).ConfigureAwait(true)).Select(l => new InvoiceMatchLine(LineFor(order, l.Item).Id, l.Quantity, l.Price))]
                            : [.. order.Lines.Where(l => l.ReceivedQuantity > l.InvoicedQuantity).Select(l => new InvoiceMatchLine(l.Id, l.ReceivedQuantity - l.InvoicedQuantity, l.UnitPrice))];
                        InvoiceSubmission submitted = Check(await disputes.SubmitAsync(new MatchInvoiceRequest
                        {
                            CompanyId = c.CompanyId, PurchaseOrderId = order.Id, InvoiceNumber = i.Optional("Invoice") ?? string.Empty, Date = i.Date("Date"), TolerancePercent = i.Number("Tolerance"),
                            PriceVarianceAccountCode = i.Optional("Variance"), Lines = lines,
                        }).ConfigureAwait(true));
                        return submitted.Matched ? $"Matched and posted ({submitted.Match!.Value.JournalNumber})." : $"Held as dispute {submitted.DisputeId}: {submitted.Reason}";
                    case "Accept":
                        MatchInvoiceResult accepted = Check(await disputes.AcceptAsync(c.CompanyId, (long)(i.OptionalNumber("Dispute") ?? 0m), c.UserId, i.Optional("Note") ?? string.Empty, i.Optional("Variance") ?? string.Empty)
                            .ConfigureAwait(true));
                        return $"Accepted and posted ({accepted.JournalNumber}).";
                    default:
                        return Check(await disputes.RejectAsync(c.CompanyId, (long)(i.OptionalNumber("Dispute") ?? 0m), c.UserId, i.Optional("Note") ?? string.Empty).ConfigureAwait(true), "Dispute rejected.");
                }
            },
            List = async c => Table(await c.Get<IInvoiceDisputeService>().OpenAsync(c.CompanyId).ConfigureAwait(true),
                ("Dispute", d => d.DisputeId), ("Supplier", d => d.SupplierId), ("Invoice", d => d.InvoiceNumber), ("Date", d => D(d.InvoiceDate)), ("Reason", d => d.Reason), ("Value", d => M(d.InvoicedValue))),
        };
        yield return new Operation
        {
            Module = module, Title = "Amend a purchase order", ListTitle = "Purchase orders",
            Description = "Changes order lines (ITEM:quantity@price; …) as a new version with the reason — quantities never below what was received, prices not once invoiced; a rise over "
                + "the threshold sends the order back for re-approval. Re-approve clears it (another user); History lists its versions.",
            Fields = [Pick("Step", "Step", ["Amend", "Re-approve", "History"]), Req("Order", "Order number"), Opt("Lines", "Lines: ITEM:quantity@price; …"), Opt("Reason", "Reason"),
                Num("Threshold", "Re-approval threshold %", "10"), Today()],
            Run = async (c, i) =>
            {
                IPurchaseOrderAmendmentService amendments = c.Get<IPurchaseOrderAmendmentService>();
                long orderId = OrderId(c, i.Text("Order"));
                switch (i.Text("Step"))
                {
                    case "Amend":
                        PurchaseOrder order = await c.Db.PurchaseOrders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == orderId).ConfigureAwait(true);
                        List<AmendedLine> lines = [.. (await OrderLinesAsync(c, i.Optional("Lines") ?? string.Empty).ConfigureAwait(true)).Select(l => new AmendedLine(LineFor(order, l.Item).Id, l.Quantity, l.Price))];
                        AmendmentResult amended = Check(await amendments.AmendAsync(c.CompanyId, orderId, new AmendmentRequest(i.Date("Date"), c.UserId, i.Optional("Reason") ?? string.Empty, lines, i.Number("Threshold")))
                            .ConfigureAwait(true));
                        return $"Version {amended.Version}: {M(amended.PreviousTotal)} → {M(amended.NewTotal)}{(amended.AwaitingReapproval ? "; awaiting re-approval" : string.Empty)}.";
                    case "Re-approve":
                        return Check(await amendments.ReapproveAsync(c.CompanyId, orderId, c.UserId).ConfigureAwait(true), "Re-approved.");
                    default:
                        IReadOnlyList<OrderVersion> versions = await amendments.HistoryAsync(c.CompanyId, orderId).ConfigureAwait(true);
                        return versions.Count == 0 ? "Never amended." : string.Join(Environment.NewLine, versions.Select(v => $"v{v.Version} {D(v.AmendedOn)} {M(v.TotalNet),12}  {v.Reason}"));
                }
            },
            List = PurchaseOrdersAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Supplier rebates", ListTitle = "Rebate agreements",
            Description = "A volume rebate agreement with a supplier for a period with spend tiers (\"0:1; 100000:2.5; 250000:4\" — from spend:rate %); Accrue books what each agreement "
                + "has earned so far.",
            Fields = [Pick("Step", "Step", ["Create", "Accrue"]), Opt("Supplier", "Supplier code (create)"), Opt("Code", "Agreement code"), Today("From", "From"), Today("To", "To"),
                Opt("Tiers", "Tiers: from spend:rate %; …"), Opt("Receivable", "Rebate receivable account (accrue)"), Opt("Income", "Rebate income account (accrue)"), Today("Date", "As at (accrue)")],
            Run = async (c, i) =>
            {
                IRebateService rebates = c.Get<IRebateService>();
                if (i.Text("Step") == "Create")
                {
                    List<One4All.Application.Procurement.RebateTier> tiers = [];
                    foreach (string part in (i.Optional("Tiers") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                        tiers.Add(bits.Length == 2 && decimal.TryParse(bits[0], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal threshold)
                            && decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal rate)
                            ? new One4All.Application.Procurement.RebateTier(threshold, rate) : throw new OperationException($"Write each tier as spend:rate % (\"{part}\")."));
                    }

                    return Check(await rebates.CreateAsync(c.CompanyId, new RebateAgreementRequest(await SupplierAsync(c, i.Optional("Supplier") ?? string.Empty).ConfigureAwait(true), i.Optional("Code") ?? string.Empty,
                        i.Date("From"), i.Date("To"), tiers)).ConfigureAwait(true), _ => "Agreement created.");
                }

                IReadOnlyList<RebatePosition> positions = Check(await rebates.AccrueAsync(c.CompanyId, i.Date("Date"), i.Optional("Receivable") ?? string.Empty, i.Optional("Income") ?? string.Empty).ConfigureAwait(true));
                return positions.Count == 0 ? "No agreements in force." : string.Join(Environment.NewLine, positions.Select(x =>
                    $"{x.Code,-12} spend {M(x.Spend),14} at {x.RatePercent:0.##}% = earned {M(x.Earned),12}  booked {M(x.Booked),12}"));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Supplier scorecard",
            Description = "Each supplier active in a period: deliveries on time against the orders' required-by dates, quality as the share of goods not returned, spend, and "
                + "the purchase price variance — invoiced above or below the order prices.",
            Fields = [Today("From", "From"), Today("To", "To")],
            Run = async (c, i) =>
            {
                IReadOnlyList<SupplierScore> scores = await c.Get<ISupplierScorecardService>().ScorecardAsync(c.CompanyId, i.Date("From"), i.Date("To")).ConfigureAwait(true);
                return scores.Count == 0 ? "No supplier activity in the period." : string.Join(Environment.NewLine, scores.Select(x =>
                    $"{x.SupplierCode,-10} {x.SupplierName,-28} deliveries {x.Deliveries,4}  on time {(x.OnTimePercent is decimal t ? $"{t:0.#}%" : "-"),7}  quality {(x.QualityPercent is decimal q ? $"{q:0.#}%" : "-"),7}  spend {M(x.Spend),14}"
                    + $"  price var {M(x.PriceVariance),10}{(x.PriceVariancePercent is decimal v ? $" ({v:+0.#;-0.#;0}%)" : string.Empty)}"));
            },
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
