using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Inventory;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Point-of-sale and stock operations.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Retail()
    {
        const string module = "Point of Sale";
        string[] kinds = [.. Enum.GetNames<PosEventKind>().Where(k => k != nameof(PosEventKind.Refund))];
        yield return new Operation
        {
            Module = module, Title = "Save a promotion", ListTitle = "Promotions",
            Description = "NForPrice (N items for a price), BuyXGetY (no reward items = lowest price free), PercentOff (items or a category), MealDeal (courses separated by '|'). Items as codes separated by ','.",
            Fields = [Req("Code", "Code"), Req("Name", "Name on receipts"), Pick("Kind", "Kind", Enum.GetNames<PromotionKind>()), Req("Account", "Discount account"), Num("Quantity", "N / X", "0"),
                Num("Reward", "Y", "0"), OptNum("Price", "Set price"), OptNum("Percent", "Percent off"), Opt("Category", "Category"), Opt("Items", "Items"), Opt("RewardItems", "Reward items"),
                Opt("Courses", "Meal courses: A,B|C|D"), Num("Priority", "Priority (lower first)", "10"), Tick("Compounding", "Stacks on other promotions"), Tick("Member", "Members only"),
                Opt("Coupon", "Coupon code"), Today("From", "Starts"), new("To", "Ends", FieldKind.Date, Required: false), Opt("DailyFrom", "Happy hour from (HH:mm)"), Opt("DailyTo", "Happy hour to (HH:mm)")],
            Run = async (c, i) =>
            {
                static IReadOnlyList<string> Codes(string? text) => [.. (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
                static TimeOnly? Clock(string? text) => text is null ? null : TimeOnly.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
                return Check(await c.Get<IPromotionService>().SaveAsync(new PromotionRequest
                {
                    CompanyId = c.CompanyId, Code = i.Text("Code"), Name = i.Text("Name"), Kind = Enum.Parse<PromotionKind>(i.Text("Kind")), DiscountAccountCode = i.Text("Account"),
                    Quantity = i.Whole("Quantity"), RewardQuantity = i.Whole("Reward"), Price = i.OptionalNumber("Price"), Percent = i.OptionalNumber("Percent"), Category = i.Optional("Category"),
                    Items = Codes(i.Optional("Items")), RewardItems = Codes(i.Optional("RewardItems")),
                    Courses = [.. (i.Optional("Courses") ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Codes)],
                    Priority = i.Whole("Priority"), Compounding = i.Flag("Compounding"), MemberOnly = i.Flag("Member"), CouponCode = i.Optional("Coupon"), StartsOn = i.Date("From"),
                    EndsOn = i.OptionalDate("To"), DailyFrom = Clock(i.Optional("DailyFrom")), DailyTo = Clock(i.Optional("DailyTo")),
                }).ConfigureAwait(true), id => $"Promotion {i.Text("Code").ToUpperInvariant()} saved.");
            },
            List = async c => Table(await c.Db.Promotions.AsNoTracking().Where(p => p.CompanyId == c.CompanyId).OrderBy(p => p.Priority).ThenBy(p => p.Code).ToListAsync().ConfigureAwait(true),
                ("Code", p => p.Code), ("Name", p => p.Name), ("Kind", p => p.Kind), ("Priority", p => p.Priority), ("Runs", p => $"{D(p.StartsOn)} – {(p.EndsOn is DateOnly e ? D(e) : "open")}"),
                ("Hours", p => p.DailyFrom is TimeOnly f ? $"{f:HH\\:mm}–{p.DailyTo:HH\\:mm}" : "all day"), ("Members", p => p.MemberOnly ? "Yes" : string.Empty), ("Coupon", p => p.CouponCode)),
        };
        yield return new Operation
        {
            Module = module, Title = "Price a basket with promotions", Description = "Shows what the running promotions take off a basket at a date and time.",
            Fields = [Req("Lines", "Lines: ITEM:quantity; … (at the default price list)"), Req("When", "When (yyyy-MM-dd HH:mm)", DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)),
                Tick("Member", "Loyalty member"), Opt("Coupons", "Coupon codes (',')"), Req("Prices", "Unit prices: ITEM:price; …")],
            Run = async (c, i) =>
            {
                Dictionary<string, decimal> prices = i.Pairs("Prices").ToDictionary(p => p.Code.ToUpperInvariant(), p => p.Quantity);
                List<BasketItem> lines = [];
                foreach ((string code, decimal quantity, DateOnly? _) in i.Pairs("Lines"))
                {
                    lines.Add(new BasketItem(await ItemAsync(c, code).ConfigureAwait(true), quantity, prices.TryGetValue(code.ToUpperInvariant(), out decimal p) ? p : throw new OperationException($"No price for {code}.")));
                }

                BasketPricing priced = await c.Get<IPromotionService>().PriceBasketAsync(new BasketRequest(c.CompanyId, null, DateTime.Parse(i.Text("When"), System.Globalization.CultureInfo.InvariantCulture),
                    i.Flag("Member"), [.. (i.Optional("Coupons") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)], lines)).ConfigureAwait(true);
                return string.Join(Environment.NewLine, priced.Lines.Select(l => $"{Q(l.Quantity),6} × item {l.ItemId,-6} gross {M(l.Gross),10}  less {M(l.Discount),8}  net {M(l.Net),10}  {string.Join(",", l.PromotionCodes)}"))
                    + Environment.NewLine + $"Savings: {string.Join("; ", priced.Applied.Select(a => $"{a.Name} {M(a.Saving)}"))}";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Refund sale lines", ListTitle = "Recent till events",
            Description = "Refunds units of a completed sale's line (by item code): revenue and tax back, money out of the refund account, goods back on the shelf if ticked. Needs a supervisor other than you.",
            Fields = [Req("Sale", "Sale number"), Req("Item", "Item code"), Num("Quantity", "Quantity"), Req("Terminal", "Till code"), Today(), Req("RefundAccount", "Refund paid from (account)", "1000"),
                Opt("TaxAccount", "Output tax account", "2200"), Req("Reason", "Reason"), Req("AuthorisedBy", "Authorised by (user name)"), Tick("Restock", "Put the goods back in stock", true)],
            Run = async (c, i) =>
            {
                Sale sale = await c.Db.Sales.AsNoTracking().Include(s => s.Lines).FirstOrDefaultAsync(s => s.CompanyId == c.CompanyId && s.SaleNumber == i.Text("Sale")).ConfigureAwait(true)
                    ?? throw new OperationException($"No sale {i.Text("Sale")}.");
                long itemId = await ItemAsync(c, i.Text("Item")).ConfigureAwait(true);
                SaleLine line = sale.Lines.FirstOrDefault(l => l.ItemId == itemId) ?? throw new OperationException($"{i.Text("Item")} is not on sale {sale.SaleNumber}.");
                long terminal = await FindAsync(c.Db.Set<PosTerminal>(), t => t.CompanyId == c.CompanyId && t.Code.ToUpper() == i.Text("Terminal").ToUpper(), t => t.Id, "till", i.Text("Terminal")).ConfigureAwait(true);
                PosRefundResult refund = Check(await c.Get<IPosEventService>().RefundAsync(new PosRefundRequest
                {
                    CompanyId = c.CompanyId, SaleId = sale.Id, TerminalId = terminal, CashierUserId = c.UserId, AuthorisedByUserId = await UserAsync(c, i.Text("AuthorisedBy")).ConfigureAwait(true),
                    Date = i.Date("Date"), RefundAccountCode = i.Text("RefundAccount"), TaxAccountCode = i.Optional("TaxAccount"), Reason = i.Text("Reason"),
                    Lines = [new PosRefundLine(line.Id, i.Number("Quantity"))], Restock = i.Flag("Restock"),
                }).ConfigureAwait(true));
                return $"Refunded {M(refund.Gross)} on {sale.SaleNumber} — journal {refund.JournalNumber}.";
            },
            List = RecentTillEventsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Record a till event", ListTitle = "Recent till events",
            Description = "Logs a void, discount, price override, no-sale, parked / resumed sale, reprint, fiscal submission or loyalty points. Overrides and no-sales need a supervisor; voids, discounts and overrides need a reason.",
            Fields = [Pick("Kind", "Event", kinds), Req("Terminal", "Till code"), Today(), Req("Description", "Item / description"), Num("Amount", "Amount (value, keyed price, points…)"),
                OptNum("Original", "List amount (discounts / overrides)"), Opt("Sale", "Sale number"), Opt("Reason", "Reason"), Opt("AuthorisedBy", "Authorised by (user name)"),
                Opt("Customer", "Loyalty card / customer"), Opt("Reference", "Reference (fiscal receipt no.)"), Opt("Outcome", "Outcome (fiscal)")],
            Run = async (c, i) =>
            {
                long terminal = await FindAsync(c.Db.Set<PosTerminal>(), t => t.CompanyId == c.CompanyId && t.Code.ToUpper() == i.Text("Terminal").ToUpper(), t => t.Id, "till", i.Text("Terminal")).ConfigureAwait(true);
                long id = Check(await c.Get<IPosEventService>().RecordAsync(new PosEventRequest
                {
                    CompanyId = c.CompanyId, TerminalId = terminal, CashierUserId = c.UserId, Kind = Enum.Parse<PosEventKind>(i.Text("Kind")), Date = i.Date("Date"), Description = i.Text("Description"),
                    Amount = i.Number("Amount"), OriginalAmount = i.OptionalNumber("Original"), SaleNumber = i.Optional("Sale"), Reason = i.Optional("Reason"),
                    AuthorisedByUserId = await OptionalUserAsync(c, i.Optional("AuthorisedBy")).ConfigureAwait(true), CustomerReference = i.Optional("Customer"), Reference = i.Optional("Reference"), Outcome = i.Optional("Outcome"),
                }).ConfigureAwait(true));
                return $"{i.Text("Kind")} recorded (event {id}).";
            },
            List = RecentTillEventsAsync,
        };
    }

    private static async Task<System.Data.DataTable> RecentTillEventsAsync(OperationContext c)
        => Table(await c.Db.PosEvents.AsNoTracking().Where(e => e.CompanyId == c.CompanyId).OrderByDescending(e => e.Id).Take(50).ToListAsync().ConfigureAwait(true),
            ("When", e => e.OccurredOnUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)), ("Event", e => e.Kind), ("Sale", e => e.SaleNumber),
            ("Description", e => e.Description), ("Amount", e => M(e.Amount)), ("Reason", e => e.Reason), ("Reference", e => e.Reference));

    private static IEnumerable<Operation> Stock()
    {
        const string module = "Stock";
        yield return new Operation
        {
            Module = module, Title = "Record batches / serials on a movement", ListTitle = "Recent movements of batch / serial items (without lots)",
            Description = "Records which batches or serial numbers a receipt, issue or transfer moved. Lots: BATCH:qty[:expiry yyyy-MM-dd]; separated by semicolons (serials are one each).",
            Fields = [Num("Movement", "Movement id (from the list)"), Req("Lots", "Lots", "B1:10")],
            Run = async (c, i) =>
            {
                List<LotQuantity> lots = [.. i.Pairs("Lots").Select(p => new LotQuantity(p.Code, p.Quantity, p.Date))];
                return Check(await c.Get<ILotTrackingService>().RecordAsync(c.CompanyId, (long)i.Number("Movement"), lots).ConfigureAwait(true), $"{lots.Count} lot(s) recorded on movement {i.Number("Movement")}.");
            },
            List = async c =>
            {
                List<long> tracked = await c.Db.Items.Where(x => x.CompanyId == c.CompanyId && x.Tracking != ItemTracking.None).Select(x => x.Id).ToListAsync().ConfigureAwait(true);
                List<long> done = await c.Db.StockLotMovements.Where(l => l.CompanyId == c.CompanyId).Select(l => l.StockMovementId).Distinct().ToListAsync().ConfigureAwait(true);
                Dictionary<long, string> items = await c.Db.Items.Where(x => tracked.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Code).ConfigureAwait(true);
                return Table(await c.Db.StockMovements.AsNoTracking().Where(m => m.CompanyId == c.CompanyId && tracked.Contains(m.ItemId) && !done.Contains(m.Id) && m.Quantity != 0m)
                        .OrderByDescending(m => m.Id).Take(50).ToListAsync().ConfigureAwait(true),
                    ("Movement", m => m.Id), ("Date", m => D(m.Date)), ("Type", m => m.Type), ("Item", m => items.GetValueOrDefault(m.ItemId)), ("Quantity", m => Q(m.Quantity)), ("Reference", m => m.Reference));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Suggest picks (first expiry, first out)", Description = "Which batches to pick for a quantity, earliest expiry first, never expired stock.",
            Fields = [Req("Item", "Item code"), Req("Warehouse", "Warehouse code"), Num("Quantity", "Quantity"), Today()],
            Run = async (c, i) =>
            {
                var (picks, shortfall) = await c.Get<ILotTrackingService>().SuggestPicksAsync(c.CompanyId, await ItemAsync(c, i.Text("Item")).ConfigureAwait(true),
                    await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true), i.Number("Quantity"), i.Date("Date")).ConfigureAwait(true);
                string list = picks.Count == 0 ? "nothing available" : string.Join(", ", picks.Select(p => $"{p.LotNumber} × {Q(p.Quantity)}{(p.ExpiryDate is DateOnly e ? $" (exp {D(e)})" : string.Empty)}"));
                return $"Pick: {list}." + (shortfall > 0m ? $" Short by {Q(shortfall)}." : string.Empty);
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Adjust stock with a reason", Description = "Sets the counted quantity on hand; the difference posts to the adjustment account under the reason code (DAMAGED, EXPIRED, THEFT…).",
            Fields = [Req("Item", "Item code"), Req("Warehouse", "Warehouse code"), Num("Counted", "Counted quantity"), Today(), Req("Account", "Adjustment account", "5000"), Opt("Reason", "Reason code"), Opt("Reference", "Reference")],
            Run = async (c, i) =>
            {
                decimal value = Check(await c.Get<IInventoryService>().AdjustAsync(c.CompanyId, await ItemAsync(c, i.Text("Item")).ConfigureAwait(true), await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true),
                    i.Date("Date"), i.Number("Counted"), i.Text("Account"), i.Optional("Reference"), i.Optional("Reason")).ConfigureAwait(true));
                return value == 0m ? "No difference to adjust." : $"Adjusted: {(value > 0m ? "surplus" : "shortage")} of {M(Math.Abs(value))}.";
            },
            List = async c => Table(await c.Db.StockReasonCodes.AsNoTracking().Where(r => r.CompanyId == c.CompanyId && r.IsActive).OrderBy(r => r.Code).ToListAsync().ConfigureAwait(true),
                ("Reason code", r => r.Code), ("Meaning", r => r.Name)),
            ListTitle = "Reason codes",
        };
        yield return new Operation
        {
            Module = module, Title = "Dispatch a transfer", ListTitle = "Transfers in transit",
            Description = "Sends stock from one warehouse towards another through a transit location; receive it at the other end when it arrives.",
            Fields = [Req("Item", "Item code"), Req("From", "From warehouse"), Req("To", "To warehouse"), Req("Transit", "Transit location"), Num("Quantity", "Quantity"), Today()],
            Run = async (c, i) =>
            {
                DispatchTransferResult sent = Check(await c.Get<IStockTransferService>().DispatchAsync(new DispatchTransferRequest
                {
                    CompanyId = c.CompanyId, ItemId = await ItemAsync(c, i.Text("Item")).ConfigureAwait(true), FromWarehouseId = await WarehouseAsync(c, i.Text("From")).ConfigureAwait(true),
                    ToWarehouseId = await WarehouseAsync(c, i.Text("To")).ConfigureAwait(true), TransitWarehouseId = await WarehouseAsync(c, i.Text("Transit")).ConfigureAwait(true), Date = i.Date("Date"), Quantity = i.Number("Quantity"),
                }).ConfigureAwait(true));
                return $"Transfer {sent.TransferNumber} dispatched ({M(sent.Value)} in transit).";
            },
            List = InTransitAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Receive a transfer", ListTitle = "Transfers in transit",
            Description = "Books what arrived into the destination; any shortfall is written off the transit location to the loss account.",
            Fields = [Req("Transfer", "Transfer number"), Num("Received", "Quantity received"), Today(), Opt("LossAccount", "Loss account (if short)", "5000")],
            Run = async (c, i) =>
            {
                long id = await FindAsync(c.Db.StockTransfers, t => t.CompanyId == c.CompanyId && t.TransferNumber == i.Text("Transfer"), t => t.Id, "transfer", i.Text("Transfer")).ConfigureAwait(true);
                decimal lost = Check(await c.Get<IStockTransferService>().ReceiveAsync(c.CompanyId, id, i.Date("Date"), i.Number("Received"), i.Optional("LossAccount")).ConfigureAwait(true));
                return lost == 0m ? $"Transfer {i.Text("Transfer")} received in full." : $"Transfer {i.Text("Transfer")} received short; {M(lost)} written off.";
            },
            List = InTransitAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Run an assembly / production batch", ListTitle = "Bills of materials",
            Description = "Consumes the BOM's components (or the usage keyed, ITEM:qty;…) and books the output into stock at their cost.",
            Fields = [Req("Bom", "BOM code"), Req("Warehouse", "Warehouse code"), Num("Planned", "Planned output"), OptNum("Actual", "Actual output (if different)"), Today(),
                Req("Clearing", "Assembly clearing account"), Opt("Usage", "Actual usage ITEM:qty;… (optional)")],
            Run = async (c, i) =>
            {
                long bom = await FindAsync(c.Db.BillsOfMaterials, b => b.CompanyId == c.CompanyId && b.Code.ToUpper() == i.Text("Bom").ToUpper(), b => b.Id, "BOM", i.Text("Bom")).ConfigureAwait(true);
                List<ComponentUsage> usage = [];
                foreach (var (code, quantity, _) in i.Pairs("Usage"))
                {
                    usage.Add(new ComponentUsage(await ItemAsync(c, code).ConfigureAwait(true), quantity));
                }

                AssemblyResult run = Check(await c.Get<IAssemblyService>().AssembleAsync(new AssemblyRequest
                {
                    CompanyId = c.CompanyId, BillOfMaterialsId = bom, WarehouseId = await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true), Date = i.Date("Date"),
                    PlannedOutput = i.Number("Planned"), ActualOutput = i.OptionalNumber("Actual"), Usage = usage, ClearingAccountCode = i.Text("Clearing"),
                }).ConfigureAwait(true));
                return $"Run {run.RunNumber}: cost {M(run.TotalCost)}, {M(run.UnitCost)} a unit.";
            },
            List = async c =>
            {
                Dictionary<long, string> items = await c.Db.Items.Where(x => x.CompanyId == c.CompanyId).ToDictionaryAsync(x => x.Id, x => x.Code).ConfigureAwait(true);
                return Table(await c.Db.BillsOfMaterials.AsNoTracking().Include(b => b.Lines).Where(b => b.CompanyId == c.CompanyId && b.IsActive).OrderBy(b => b.Code).ToListAsync().ConfigureAwait(true),
                    ("BOM", b => b.Code), ("Makes", b => items.GetValueOrDefault(b.ParentItemId)), ("Per batch", b => Q(b.OutputQuantity)),
                    ("Components", b => string.Join(", ", b.Lines.Select(l => $"{items.GetValueOrDefault(l.ComponentItemId)} × {Q(l.Quantity)}"))));
            },
        };
    }

    private static async Task<System.Data.DataTable> InTransitAsync(OperationContext c)
    {
        Dictionary<long, string> items = await c.Db.Items.Where(x => x.CompanyId == c.CompanyId).ToDictionaryAsync(x => x.Id, x => x.Code).ConfigureAwait(true);
        Dictionary<long, string> warehouses = await c.Db.Warehouses.Where(x => x.CompanyId == c.CompanyId).ToDictionaryAsync(x => x.Id, x => x.Code).ConfigureAwait(true);
        return Table(await c.Db.StockTransfers.AsNoTracking().Where(t => t.CompanyId == c.CompanyId && t.Status == StockTransferStatus.InTransit).OrderBy(t => t.DispatchedOn).ToListAsync().ConfigureAwait(true),
            ("Transfer", t => t.TransferNumber), ("Dispatched", t => D(t.DispatchedOn)), ("Item", t => items.GetValueOrDefault(t.ItemId)), ("From", t => warehouses.GetValueOrDefault(t.FromWarehouseId)),
            ("To", t => warehouses.GetValueOrDefault(t.ToWarehouseId)), ("Quantity", t => Q(t.Quantity)), ("Value", t => M(t.Value)));
    }
}
