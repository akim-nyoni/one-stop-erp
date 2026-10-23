using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Common;
using One4All.Core.Restaurant;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Restaurant;

/// <summary>
/// The Restaurant / Hospitality module. Menu items are sellable composites (never stocked); billing a table
/// order recognises revenue at the menu prices and cost of sales by exploding each item's recipe into
/// moving-average stock issues of its ingredients - everything posts through the central engine.
/// </summary>
public sealed class RestaurantService : IRestaurantService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;
    private readonly IInventoryService _inventory;
    private readonly Application.Abstractions.Trading.ISalesLedgerService _sales;
    private readonly Application.Abstractions.Vouchers.IVoucherService _vouchers;
    private readonly Application.Abstractions.Security.IPermissionService? _permissions;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    /// <param name="inventory">The inventory service (recipe explosion → stock issues).</param>
    /// <param name="sales">The sales ledger (house-account / charge-to-room billing).</param>
    /// <param name="vouchers">The voucher service (gift-card tender).</param>
    /// <param name="permissions">The permission service (a supervisor's right to void fired food or reverse a bill).</param>
    public RestaurantService(One4AllDbContext db, IPostingEngine posting, IInventoryService inventory, Application.Abstractions.Trading.ISalesLedgerService sales, Application.Abstractions.Vouchers.IVoucherService vouchers,
        Application.Abstractions.Security.IPermissionService? permissions = null)
    {
        _permissions = permissions;
        _db = db;
        _posting = posting;
        _inventory = inventory;
        _sales = sales;
        _vouchers = vouchers;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateMenuItemAsync(long companyId, string code, string name, decimal price, string revenueAccountCode, string costOfSalesAccountCode, IReadOnlyList<RecipeIngredient> ingredients, string? taxCode = null, CancellationToken ct = default)
    {
        MenuItem item = new(companyId, code, name, price, revenueAccountCode, costOfSalesAccountCode, taxCode);
        foreach (RecipeIngredient ingredient in ingredients)
        {
            item.AddIngredient(ingredient.IngredientItemId, ingredient.Quantity);
        }

        _db.Set<MenuItem>().Add(item);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return item.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateTableAsync(long companyId, string tableNumber, int seats, CancellationToken ct = default)
    {
        RestaurantTable table = new(companyId, tableNumber, seats);
        _db.Set<RestaurantTable>().Add(table);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return table.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> OpenOrderAsync(long companyId, long tableId, string orderNumber, long waiterUserId, DateOnly date, int partySize = 1, CancellationToken ct = default)
    {
        RestaurantTable? table = await _db.Set<RestaurantTable>().FirstOrDefaultAsync(t => t.Id == tableId && t.CompanyId == companyId, ct).ConfigureAwait(false);
        if (table is null)
        {
            return Error.NotFound("Res.TableNotFound", $"Table {tableId} not found.");
        }

        // A combined table is seated through its primary; a non-primary member cannot take its own order.
        if (table.GroupId is { } groupId)
        {
            TableGroup? group = await _db.Set<TableGroup>().FirstOrDefaultAsync(g => g.Id == groupId && g.Status == TableGroupStatus.Active, ct).ConfigureAwait(false);
            if (group is not null && group.PrimaryTableId != table.Id)
            {
                return Error.Rule("Res.TableInGroup", $"Table {table.TableNumber} is combined into a group; open the order on the primary table.");
            }
        }

        if (table.Status != TableStatus.Free)
        {
            return Error.Rule("Res.TableOccupied", $"Table {table.TableNumber} already has an open order.");
        }

        TableOrder order = new(companyId, tableId, orderNumber, waiterUserId, date, partySize);
        _db.Set<TableOrder>().Add(order);
        await OccupyTableAndGroupAsync(companyId, table, ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return order.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> OpenOffPremiseOrderAsync(long companyId, string orderNumber, OrderType orderType, long waiterUserId, DateOnly date, string? customerName = null, string? customerPhone = null, string? deliveryAddress = null, CancellationToken ct = default)
    {
        if (orderType == OrderType.DineIn)
        {
            return Error.Rule("Res.DineInNeedsTable", "A dine-in order must be opened at a table; use OpenOrderAsync.");
        }

        TableOrder order = new(companyId, orderNumber, orderType, waiterUserId, date, customerName, customerPhone, deliveryAddress);
        _db.Set<TableOrder>().Add(order);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return order.Id;
    }

    /// <inheritdoc />
    public async Task<Result> AssignDeliveryDriverAsync(long companyId, long orderId, long driverUserId, CancellationToken ct = default)
    {
        TableOrder? order = await _db.Set<TableOrder>().FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Res.OrderNotFound", $"Order {orderId} not found."));
        }

        if (order.OrderType != OrderType.Delivery || order.DeliveryStatus is null)
        {
            return Result.Failure(Error.Rule("Res.NotADelivery", "Only a delivery order can have a driver assigned."));
        }

        if (order.DeliveryStatus is DeliveryStatus.Delivered or DeliveryStatus.Failed)
        {
            return Result.Failure(Error.Rule("Res.DeliveryClosed", "The delivery is already completed."));
        }

        order.AssignDriver(driverUserId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> AdvanceDeliveryAsync(long companyId, long orderId, DeliveryStatus status, CancellationToken ct = default)
    {
        TableOrder? order = await _db.Set<TableOrder>().FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Res.OrderNotFound", $"Order {orderId} not found."));
        }

        if (order.OrderType != OrderType.Delivery || order.DeliveryStatus is null)
        {
            return Result.Failure(Error.Rule("Res.NotADelivery", "Only a delivery order has a dispatch state."));
        }

        if (!order.AdvanceDelivery(status))
        {
            return Result.Failure(Error.Rule("Res.DeliveryNotAdvanced", $"The delivery cannot move from {order.DeliveryStatus} to {status}."));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> NoteLineAsync(long companyId, long orderId, long lineId, string? note, CancellationToken ct = default)
    {
        TableOrder? order = await _db.TableOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        TableOrderLine? line = order?.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
        {
            return Result.Failure(Error.NotFound("Rst.LineNotFound", $"Order {orderId} has no line {lineId}."));
        }

        line.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<decimal>> AddItemAsync(long companyId, long orderId, long menuItemId, decimal quantity, decimal taxAmount = 0m, MenuCourse course = MenuCourse.Main, int? seatNumber = null, string? priceLevel = null, CancellationToken ct = default)
    {
        if (quantity <= 0m)
        {
            return Error.Rule("Res.InvalidQuantity", "The quantity must be positive.");
        }

        TableOrder? order = await _db.Set<TableOrder>().Include(o => o.Lines).ThenInclude(l => l.Modifiers).FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Res.OrderNotFound", $"Order {orderId} not found.");
        }

        if (order.Status != TableOrderStatus.Open)
        {
            return Error.Rule("Res.OrderNotOpen", "Items can only be added to an open order.");
        }

        MenuItem? menu = await _db.Set<MenuItem>().Include(m => m.Prices).FirstOrDefaultAsync(m => m.Id == menuItemId && m.CompanyId == companyId, ct).ConfigureAwait(false);
        if (menu is null)
        {
            return Error.NotFound("Res.MenuItemNotFound", $"Menu item {menuItemId} not found.");
        }

        if (!menu.IsAvailable)
        {
            return Error.Rule("Res.MenuItemUnavailable", $"Menu item {menu.Code} is not available.");
        }

        // Enforce the bar-tab spend limit against the running net (existing lines + this one).
        if (order.SpendLimit is { } limit)
        {
            decimal prospective = order.NetTotal + decimal.Round(quantity * menu.PriceFor(priceLevel), 4, MidpointRounding.ToEven);
            if (prospective > limit)
            {
                return Error.Rule("Res.SpendLimitExceeded", $"Adding this item would take the tab to {prospective:0.00}, over the {limit:0.00} limit.");
            }
        }

        TableOrderLine line = order.AddLine(menu.Id, menu.Name, quantity, menu.PriceFor(priceLevel), taxAmount, menu.RevenueAccountCode, menu.CostOfSalesAccountCode, course, seatNumber);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return line.LineNet;
    }

    /// <inheritdoc />
    public async Task<Result> SetOrderSpendLimitAsync(long companyId, long orderId, decimal? limit, CancellationToken ct = default)
    {
        if (limit is < 0m)
        {
            return Result.Failure(Error.Rule("Res.SpendLimitNegative", "The spend limit cannot be negative."));
        }

        TableOrder? order = await _db.Set<TableOrder>().FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Res.OrderNotFound", $"Order {orderId} not found."));
        }

        if (order.Status != TableOrderStatus.Open)
        {
            return Result.Failure(Error.Rule("Res.OrderNotOpen", "Only an open order's spend limit can be set."));
        }

        order.SetSpendLimit(limit);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetMenuItemPriceLevelAsync(long companyId, long menuItemId, string priceLevel, decimal price, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(priceLevel))
        {
            return Result.Failure(Error.Validation("Res.PriceLevelRequired", "A price level is required."));
        }

        if (price < 0m)
        {
            return Result.Failure(Error.Rule("Res.PriceLevelNegative", "A price-level price cannot be negative."));
        }

        MenuItem? menu = await _db.Set<MenuItem>().Include(m => m.Prices).FirstOrDefaultAsync(m => m.Id == menuItemId && m.CompanyId == companyId, ct).ConfigureAwait(false);
        if (menu is null)
        {
            return Result.Failure(Error.NotFound("Res.MenuItemNotFound", $"Menu item {menuItemId} not found."));
        }

        menu.SetPrice(priceLevel, price);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<decimal>> RecordWastageAsync(long companyId, long itemId, long warehouseId, DateOnly date, decimal quantity, string wastageAccountCode, string reason, CancellationToken ct = default)
        => _db.AtomicAsync(() => RecordWastageCoreAsync(companyId, itemId, warehouseId, date, quantity, wastageAccountCode, reason, allowNegative: false, ct), ct);

    // One unit of work: the bill's journal(s), stock issues, tender and order updates commit together or not at all.
    // allowNegative: a dish already fired was made from what the kitchen had, so its write-off issues like billing does (wastage counted at the shelf passes false).
    private async Task<Result<decimal>> RecordWastageCoreAsync(long companyId, long itemId, long warehouseId, DateOnly date, decimal quantity, string wastageAccountCode, string reason, bool allowNegative,
        CancellationToken ct)
    {
        if (quantity <= 0m)
        {
            return Error.Rule("Res.WastageInvalidQuantity", "The wastage quantity must be positive.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Res.WastageNoReason", "Recording wastage needs a reason.");
        }

        if (string.IsNullOrWhiteSpace(wastageAccountCode))
        {
            return Error.Validation("Res.WastageNoAccount", "Recording wastage needs a wastage expense account.");
        }

        Core.Inventory.Item? item = await _db.Set<Core.Inventory.Item>().FirstOrDefaultAsync(i => i.Id == itemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound("Res.ItemNotFound", $"Item {itemId} not found.");
        }

        if (string.IsNullOrWhiteSpace(item.CostOfSalesAccountCode))
        {
            return Error.Rule("Res.ItemNoCosAccount", $"Item {item.Code} has no cost-of-sales account to reclassify from.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Res.CompanyNotFound", $"Company {companyId} not found.");
        }

        // Issue the stock (Dr cost of sales / Cr stock at moving average); wastage of stock you do not have is refused.
        Result<StockMovementResult> issue = await IssueAsync(companyId, itemId, warehouseId, date, quantity, $"WASTE:{reason}", ct, allowNegative).ConfigureAwait(false);
        if (issue.IsFailure)
        {
            return issue.Error;
        }

        decimal value = issue.Value.Value;
        if (value > 0m)
        {
            // Reclassify the cost from cost of sales to the wastage expense account (Dr wastage / Cr COS).
            Result<PostingReceipt> reclass = await _posting.PostAsync(new JournalRequest
            {
                CompanyId = companyId,
                SourceModule = "RES",
                PostingDate = date,
                TransactionCurrency = company.BaseCurrency,
                DocumentType = "RES-WASTE",
                Narrative = $"Wastage {item.Code}: {reason}",
                Lines =
                [
                    new JournalLineRequest { AccountCode = wastageAccountCode, Side = JournalSide.Debit, TransactionAmount = value },
                    new JournalLineRequest { AccountCode = item.CostOfSalesAccountCode!, Side = JournalSide.Credit, TransactionAmount = value },
                ],
            }, ct).ConfigureAwait(false);

            if (reclass.IsFailure)
            {
                return reclass.Error;
            }
        }

        _db.Set<WastageRecord>().Add(new WastageRecord(companyId, itemId, warehouseId, date, quantity, value, reason, wastageAccountCode));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return value;
    }

    /// <inheritdoc />
    public async Task<Result<decimal>> AddItemModifierAsync(long companyId, long orderId, long lineId, string description, decimal priceDelta, long? ingredientItemId = null, decimal ingredientQuantity = 0m, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return Error.Validation("Res.ModifierNoDescription", "A modifier needs a description.");
        }

        if (ingredientItemId is not null && ingredientQuantity <= 0m)
        {
            return Error.Rule("Res.ModifierInvalidQuantity", "A modifier ingredient needs a positive quantity.");
        }

        TableOrder? order = await _db.Set<TableOrder>().Include(o => o.Lines).ThenInclude(l => l.Modifiers)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Res.OrderNotFound", $"Order {orderId} not found.");
        }

        if (order.Status != TableOrderStatus.Open)
        {
            return Error.Rule("Res.OrderNotOpen", "Modifiers can only be added to an open order.");
        }

        TableOrderLine? line = order.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
        {
            return Error.NotFound("Res.LineNotFound", $"Order line {lineId} is not on this order.");
        }

        if (line.BilledQuantity > 0m)
        {
            return Error.Rule("Res.LineAlreadyBilled", "A line that has already been billed cannot be modified.");
        }

        line.AddModifier(description, priceDelta, ingredientItemId, ingredientQuantity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return line.LineNet;
    }

    /// <inheritdoc />
    public Task<Result<BillResult>> BillOrderAsync(long companyId, long orderId, long warehouseId, DateOnly date, string cashAccountCode, string? taxAccountCode = null, decimal serviceChargeAmount = 0m, string? serviceChargeAccountCode = null, decimal tipAmount = 0m, string? tipsPayableAccountCode = null, decimal discountAmount = 0m, string? discountAccountCode = null, long? tillSessionId = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => BillOrderCoreAsync(companyId, orderId, warehouseId, date, cashAccountCode, taxAccountCode, serviceChargeAmount, serviceChargeAccountCode, tipAmount, tipsPayableAccountCode, discountAmount, discountAccountCode, tillSessionId, ct), ct);

    // One unit of work: the bill's journal(s), stock issues, tender and order updates commit together or not at all.
    private async Task<Result<BillResult>> BillOrderCoreAsync(long companyId, long orderId, long warehouseId, DateOnly date, string cashAccountCode, string? taxAccountCode, decimal serviceChargeAmount, string? serviceChargeAccountCode, decimal tipAmount, string? tipsPayableAccountCode, decimal discountAmount, string? discountAccountCode, long? tillSessionId, CancellationToken ct)
    {
        Result<TableOrder> loaded = await LoadOpenOrderAsync(companyId, orderId, ct).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        TableOrder order = loaded.Value;
        // Bill everything still outstanding on the order (voided lines are excluded).
        List<(TableOrderLine Line, decimal Quantity)> portions = order.Lines
            .Where(l => !l.IsVoided && l.RemainingQuantity > 0m)
            .Select(l => (l, l.RemainingQuantity))
            .ToList();
        if (portions.Count == 0)
        {
            return Error.Rule("Res.NothingToBill", "The order has nothing left to bill.");
        }

        return await PostBillAsync(companyId, order, warehouseId, date, portions, cashAccountCode, taxAccountCode, serviceChargeAmount, serviceChargeAccountCode, tipAmount, tipsPayableAccountCode, discountAmount, discountAccountCode, ct, tillSessionId: tillSessionId, cashIntoTill: true).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<BillResult>> BillWithTendersAsync(long companyId, long orderId, long warehouseId, DateOnly date, IReadOnlyList<TenderLine> tenders, string? taxAccountCode = null, decimal serviceChargeAmount = 0m, string? serviceChargeAccountCode = null, decimal tipAmount = 0m, string? tipsPayableAccountCode = null, decimal discountAmount = 0m, string? discountAccountCode = null, long? tillSessionId = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => BillWithTendersCoreAsync(companyId, orderId, warehouseId, date, tenders, taxAccountCode, serviceChargeAmount, serviceChargeAccountCode, tipAmount, tipsPayableAccountCode, discountAmount, discountAccountCode, tillSessionId, ct), ct);

    // One unit of work: the bill's journal(s), stock issues, tender and order updates commit together or not at all.
    private async Task<Result<BillResult>> BillWithTendersCoreAsync(long companyId, long orderId, long warehouseId, DateOnly date, IReadOnlyList<TenderLine> tenders, string? taxAccountCode, decimal serviceChargeAmount, string? serviceChargeAccountCode, decimal tipAmount, string? tipsPayableAccountCode, decimal discountAmount, string? discountAccountCode, long? tillSessionId, CancellationToken ct)
    {
        if (tenders.Count == 0)
        {
            return Error.Validation("Res.NoTenders", "A split-tender bill needs at least one tender.");
        }

        if (tenders.Any(t => t.Amount < 0m || string.IsNullOrWhiteSpace(t.AccountCode)))
        {
            return Error.Rule("Res.InvalidTender", "Each tender needs a non-negative amount and an account.");
        }

        Result<TableOrder> loaded = await LoadOpenOrderAsync(companyId, orderId, ct).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        TableOrder order = loaded.Value;
        List<(TableOrderLine Line, decimal Quantity)> portions = order.Lines
            .Where(l => !l.IsVoided && l.RemainingQuantity > 0m)
            .Select(l => (l, l.RemainingQuantity))
            .ToList();
        if (portions.Count == 0)
        {
            return Error.Rule("Res.NothingToBill", "The order has nothing left to bill.");
        }

        // The first tender account is only a fallback label; PostBillAsync uses the tender list for the debits.
        return await PostBillAsync(companyId, order, warehouseId, date, portions, tenders[0].AccountCode, taxAccountCode, serviceChargeAmount, serviceChargeAccountCode, tipAmount, tipsPayableAccountCode, discountAmount, discountAccountCode, ct, tenders, tillSessionId, cashIntoTill: true).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<BillResult>> BillPartialAsync(long companyId, long orderId, long warehouseId, DateOnly date, IReadOnlyList<SplitPortion> portions, string cashAccountCode, string? taxAccountCode = null, decimal serviceChargeAmount = 0m, string? serviceChargeAccountCode = null, decimal tipAmount = 0m, string? tipsPayableAccountCode = null, decimal discountAmount = 0m, string? discountAccountCode = null, long? tillSessionId = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => BillPartialCoreAsync(companyId, orderId, warehouseId, date, portions, cashAccountCode, taxAccountCode, serviceChargeAmount, serviceChargeAccountCode, tipAmount, tipsPayableAccountCode, discountAmount, discountAccountCode, tillSessionId, ct), ct);

    // One unit of work: the bill's journal(s), stock issues, tender and order updates commit together or not at all.
    private async Task<Result<BillResult>> BillPartialCoreAsync(long companyId, long orderId, long warehouseId, DateOnly date, IReadOnlyList<SplitPortion> portions, string cashAccountCode, string? taxAccountCode, decimal serviceChargeAmount, string? serviceChargeAccountCode, decimal tipAmount, string? tipsPayableAccountCode, decimal discountAmount, string? discountAccountCode, long? tillSessionId, CancellationToken ct)
    {
        if (portions.Count == 0)
        {
            return Error.Validation("Res.NoPortions", "A split bill needs at least one line portion.");
        }

        Result<TableOrder> loaded = await LoadOpenOrderAsync(companyId, orderId, ct).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        TableOrder order = loaded.Value;
        List<(TableOrderLine Line, decimal Quantity)> resolved = [];
        foreach (SplitPortion portion in portions)
        {
            TableOrderLine? line = order.Lines.FirstOrDefault(l => l.Id == portion.OrderLineId);
            if (line is null)
            {
                return Error.NotFound("Res.LineNotFound", $"Order line {portion.OrderLineId} is not on this order.");
            }

            if (line.IsVoided)
            {
                return Error.Rule("Res.LineVoided", $"Order line {portion.OrderLineId} was voided and cannot be billed.");
            }

            if (portion.Quantity <= 0m || portion.Quantity > line.RemainingQuantity + 0.0001m)
            {
                return Error.Rule("Res.InvalidPortion", $"Cannot bill {portion.Quantity} of '{line.Description}'; {line.RemainingQuantity} remains.");
            }

            resolved.Add((line, portion.Quantity));
        }

        return await PostBillAsync(companyId, order, warehouseId, date, resolved, cashAccountCode, taxAccountCode, serviceChargeAmount, serviceChargeAccountCode, tipAmount, tipsPayableAccountCode, discountAmount, discountAccountCode, ct, tillSessionId: tillSessionId, cashIntoTill: true).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> AssignLineToSeatAsync(long companyId, long orderId, long lineId, int? seatNumber, CancellationToken ct = default)
    {
        if (seatNumber is <= 0)
        {
            return Result.Failure(Error.Rule("Res.InvalidSeat", "The seat number must be positive."));
        }

        TableOrder? order = await _db.Set<TableOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Res.OrderNotFound", $"Order {orderId} not found."));
        }

        if (order.Status != TableOrderStatus.Open)
        {
            return Result.Failure(Error.Rule("Res.OrderNotOpen", "Only a line on an open order can be reseated."));
        }

        TableOrderLine? line = order.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
        {
            return Result.Failure(Error.NotFound("Res.LineNotFound", $"Order line {lineId} is not on this order."));
        }

        line.AssignSeat(seatNumber);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<BillResult>> BillSeatAsync(long companyId, long orderId, int seatNumber, long warehouseId, DateOnly date, string cashAccountCode, string? taxAccountCode = null, decimal serviceChargeAmount = 0m, string? serviceChargeAccountCode = null, decimal tipAmount = 0m, string? tipsPayableAccountCode = null, decimal discountAmount = 0m, string? discountAccountCode = null, long? tillSessionId = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => BillSeatCoreAsync(companyId, orderId, seatNumber, warehouseId, date, cashAccountCode, taxAccountCode, serviceChargeAmount, serviceChargeAccountCode, tipAmount, tipsPayableAccountCode, discountAmount, discountAccountCode, tillSessionId, ct), ct);

    // One unit of work: the bill's journal(s), stock issues, tender and order updates commit together or not at all.
    private async Task<Result<BillResult>> BillSeatCoreAsync(long companyId, long orderId, int seatNumber, long warehouseId, DateOnly date, string cashAccountCode, string? taxAccountCode, decimal serviceChargeAmount, string? serviceChargeAccountCode, decimal tipAmount, string? tipsPayableAccountCode, decimal discountAmount, string? discountAccountCode, long? tillSessionId, CancellationToken ct)
    {
        Result<TableOrder> loaded = await LoadOpenOrderAsync(companyId, orderId, ct).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        TableOrder order = loaded.Value;
        List<(TableOrderLine Line, decimal Quantity)> portions = order.Lines
            .Where(l => l.SeatNumber == seatNumber && !l.IsVoided && l.RemainingQuantity > 0m)
            .Select(l => (l, l.RemainingQuantity))
            .ToList();
        if (portions.Count == 0)
        {
            return Error.Rule("Res.NothingForSeat", $"Seat {seatNumber} has nothing left to bill.");
        }

        return await PostBillAsync(companyId, order, warehouseId, date, portions, cashAccountCode, taxAccountCode, serviceChargeAmount, serviceChargeAccountCode, tipAmount, tipsPayableAccountCode, discountAmount, discountAccountCode, ct, tillSessionId: tillSessionId, cashIntoTill: true).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<BillResult>> BillToVoucherAsync(long companyId, long orderId, string cardNumber, long warehouseId, DateOnly date, string giftCardLiabilityAccountCode, string? taxAccountCode = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => BillToVoucherCoreAsync(companyId, orderId, cardNumber, warehouseId, date, giftCardLiabilityAccountCode, taxAccountCode, ct), ct);

    // One unit of work: the bill's journal(s), stock issues, tender and order updates commit together or not at all.
    private async Task<Result<BillResult>> BillToVoucherCoreAsync(long companyId, long orderId, string cardNumber, long warehouseId, DateOnly date, string giftCardLiabilityAccountCode, string? taxAccountCode, CancellationToken ct)
    {
        Result<TableOrder> loaded = await LoadOpenOrderAsync(companyId, orderId, ct).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        TableOrder order = loaded.Value;
        List<(TableOrderLine Line, decimal Quantity)> portions = order.Lines
            .Where(l => !l.IsVoided && l.RemainingQuantity > 0m)
            .Select(l => (l, l.RemainingQuantity))
            .ToList();
        if (portions.Count == 0)
        {
            return Error.Rule("Res.NothingToBill", "The order has nothing left to bill.");
        }

        // Amount the card must cover (net + tax of the non-comped lines).
        decimal gross = 0m;
        foreach ((TableOrderLine line, decimal quantity) in portions)
        {
            if (line.IsComped)
            {
                continue;
            }

            gross += decimal.Round(quantity * line.EffectiveUnitPrice, 4, MidpointRounding.ToEven);
            gross += line.Quantity == 0m ? 0m : decimal.Round(line.TaxAmount * quantity / line.Quantity, 4, MidpointRounding.ToEven);
        }

        if (gross > 0m)
        {
            Result<Application.Abstractions.Vouchers.GiftCardResult> balance = await _vouchers.GetBalanceAsync(companyId, cardNumber, ct).ConfigureAwait(false);
            if (balance.IsFailure)
            {
                return balance.Error;
            }

            if (balance.Value.Balance < gross)
            {
                return Error.Rule("Res.VoucherInsufficient", $"Gift card {cardNumber} has {balance.Value.Balance:0.00}, which does not cover the bill of {gross:0.00}.");
            }
        }

        // The tender debits the gift-card liability (like cash), then the card balance is reduced.
        Result<BillResult> bill = await PostBillAsync(companyId, order, warehouseId, date, portions, giftCardLiabilityAccountCode, taxAccountCode, 0m, null, 0m, null, 0m, null, ct).ConfigureAwait(false);
        if (bill.IsFailure)
        {
            return bill.Error;
        }

        if (bill.Value.Gross > 0m)
        {
            Result<Application.Abstractions.Vouchers.GiftCardResult> redeemed = await _vouchers.RedeemAsync(companyId, cardNumber, bill.Value.Gross, ct).ConfigureAwait(false);
            if (redeemed.IsFailure)
            {
                return redeemed.Error;
            }
        }

        return bill;
    }

    /// <inheritdoc />
    public Task<Result<string>> ReverseBillAsync(long companyId, long billId, DateOnly date, string reason, long operatorUserId, long? authorisedByUserId, long? refundTillSessionId = null,
        CancellationToken ct = default)
        => _db.AtomicAsync(() => ReverseBillCoreAsync(companyId, billId, date, reason, operatorUserId, authorisedByUserId, refundTillSessionId, ct), ct);

    // One unit of work: the bill's journal(s), stock issues, tender, till and order updates commit together or not at all.
    private async Task<Result<string>> ReverseBillCoreAsync(long companyId, long billId, DateOnly date, string reason, long operatorUserId, long? authorisedByUserId, long? refundTillSessionId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Res.ReasonRequired", "Reversing a bill requires a reason.");
        }

        Result authorised = await SupervisorAsync(authorisedByUserId, operatorUserId, "Reversing a bill", ct).ConfigureAwait(false);
        if (authorised.IsFailure)
        {
            return authorised.Error;
        }

        RestaurantBill? bill = await _db.Set<RestaurantBill>().Include(b => b.Postings).Include(b => b.Issues)
            .FirstOrDefaultAsync(b => b.Id == billId && b.CompanyId == companyId, ct).ConfigureAwait(false);
        if (bill is null)
        {
            return Error.NotFound("Res.BillNotFound", $"Bill {billId} not found.");
        }

        if (bill.IsReversed)
        {
            return Error.Rule("Res.BillAlreadyReversed", $"Bill {billId} has already been reversed.");
        }

        if (!bill.TenderReversible)
        {
            return Error.Rule("Res.BillNotReversible", "A house-account bill is reversed through the AR credit-note flow.");
        }

        // Reverse the revenue journal (post the exact inverse of the captured lines).
        string reversalJournal = string.Empty;
        if (bill.Postings.Count > 0)
        {
            Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
            Result<PostingReceipt> reversal = await _posting.PostAsync(new JournalRequest
            {
                CompanyId = companyId,
                SourceModule = "RES",
                PostingDate = date,
                TransactionCurrency = company.BaseCurrency,
                DocumentType = "RES-BILL-REV",
                Narrative = $"Reverse bill {bill.RevenueJournalNumber}: {reason}",
                Lines = bill.Postings.Select(p => new JournalLineRequest
                {
                    AccountCode = p.AccountCode,
                    Side = p.IsDebit ? JournalSide.Credit : JournalSide.Debit, // inverse
                    TransactionAmount = p.Amount,
                }).ToList(),
            }, ct).ConfigureAwait(false);

            if (reversal.IsFailure)
            {
                return reversal.Error;
            }

            reversalJournal = reversal.Value.JournalNumber;
        }

        // The cash goes back out of the drawer: of the bill's own session while it is open, else of an open session of the same till.
        Result<long?> refundedFrom = await RefundBillCashAsync(bill, refundTillSessionId, operatorUserId, authorisedByUserId, date, reversalJournal, ct).ConfigureAwait(false);
        if (refundedFrom.IsFailure)
        {
            return refundedFrom.Error;
        }

        // Reverse each cost-of-sales stock issue back into inventory.
        foreach (RestaurantBillIssue issue in bill.Issues)
        {
            Result<StockMovementResult> reversedIssue = await _inventory.ReverseIssueAsync(companyId, issue.StockMovementId, date, $"REV-BILL:{billId}", ct).ConfigureAwait(false);
            if (reversedIssue.IsFailure)
            {
                return reversedIssue.Error;
            }
        }

        bill.MarkReversed(date, reversalJournal, authorisedByUserId, refundedFrom.Value);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return reversalJournal;
    }

    /// <inheritdoc />
    public Task<Result<BillResult>> BillToHouseAccountAsync(long companyId, long orderId, long customerId, long warehouseId, DateOnly date, string salesAccountCode, string? taxAccountCode = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => BillToHouseAccountCoreAsync(companyId, orderId, customerId, warehouseId, date, salesAccountCode, taxAccountCode, ct), ct);

    // One unit of work: the bill's journal(s), stock issues, tender and order updates commit together or not at all.
    private async Task<Result<BillResult>> BillToHouseAccountCoreAsync(long companyId, long orderId, long customerId, long warehouseId, DateOnly date, string salesAccountCode, string? taxAccountCode, CancellationToken ct)
    {
        Result<TableOrder> loaded = await LoadOpenOrderAsync(companyId, orderId, ct).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        TableOrder order = loaded.Value;
        List<(TableOrderLine Line, decimal Quantity)> portions = order.Lines
            .Where(l => !l.IsVoided && l.RemainingQuantity > 0m)
            .Select(l => (l, l.RemainingQuantity))
            .ToList();
        if (portions.Count == 0)
        {
            return Error.Rule("Res.NothingToBill", "The order has nothing left to bill.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Res.CompanyNotFound", $"Company {companyId} not found.");
        }

        // Net and tax for the charge, excluding comped lines (they cost but do not invoice).
        decimal net = 0m;
        decimal tax = 0m;
        foreach ((TableOrderLine line, decimal quantity) in portions)
        {
            if (line.IsComped)
            {
                continue;
            }

            net += decimal.Round(quantity * line.EffectiveUnitPrice, 4, MidpointRounding.ToEven);
            tax += line.Quantity == 0m ? 0m : decimal.Round(line.TaxAmount * quantity / line.Quantity, 4, MidpointRounding.ToEven);
        }

        string invoiceJournal = string.Empty;
        if (net + tax > 0m)
        {
            Result<Application.Abstractions.Trading.TradingPostingResult> invoice = await _sales.PostInvoiceAsync(new Application.Abstractions.Trading.SalesInvoiceRequest
            {
                CompanyId = companyId,
                CustomerId = customerId,
                DocumentNumber = order.OrderNumber,
                DocumentDate = date,
                Currency = company.BaseCurrency,
                NetAmount = net,
                TaxAmount = tax,
                SalesAccountCode = salesAccountCode,
                TaxAccountCode = taxAccountCode,
                Narrative = $"Restaurant house account {order.OrderNumber}",
            }, ct).ConfigureAwait(false);

            if (invoice.IsFailure)
            {
                return invoice.Error;
            }

            invoiceJournal = invoice.Value.JournalNumber;
        }

        Result<(decimal Cost, List<long> MovementIds)> cost = await IssueCostOfSalesAsync(companyId, order, warehouseId, date, portions, ct).ConfigureAwait(false);
        if (cost.IsFailure)
        {
            return cost.Error;
        }

        bool fullyBilled = await CloseIfFullyBilledAsync(companyId, order, invoiceJournal, ct).ConfigureAwait(false);
        // A house-account charge is reversed through the AR credit-note flow, not the restaurant reversal.
        _db.Set<RestaurantBill>().Add(new RestaurantBill(companyId, order.Id, date, net, tax, 0m, 0m, 0m, net + tax, cost.Value.Cost, invoiceJournal, tenderReversible: false));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new BillResult(net, tax, 0m, 0m, 0m, net + tax, cost.Value.Cost, fullyBilled, invoiceJournal);
    }

    /// <inheritdoc />
    public Task<Result<string>> PayTipsAsync(long companyId, decimal amount, DateOnly date, string tipsPayableAccountCode, string fundingAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => PayTipsCoreAsync(companyId, amount, date, tipsPayableAccountCode, fundingAccountCode, ct), ct);

    // One unit of work: the bill's journal(s), stock issues, tender and order updates commit together or not at all.
    private async Task<Result<string>> PayTipsCoreAsync(long companyId, decimal amount, DateOnly date, string tipsPayableAccountCode, string fundingAccountCode, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return Error.Rule("Res.NonPositiveAmount", "The tips payout must be positive.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Res.CompanyNotFound", $"Company {companyId} not found.");
        }

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "RES",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "RES-TIPS",
            Narrative = "Tips paid out to staff",
            Lines =
            [
                new JournalLineRequest { AccountCode = tipsPayableAccountCode, Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = fundingAccountCode, Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);

        return posted.IsFailure ? posted.Error : posted.Value.JournalNumber;
    }

    // The cash of a bill is taken in an open session of a till of the company: never in none, nor in one already cashed up (its count and Z are done),
    // nor - where the caller names them - in another till's session or another cashier's.
    private async Task<Result> OpenTillSessionAsync(long companyId, long? tillSessionId, CancellationToken ct, long? terminalId = null, long? cashierUserId = null)
    {
        if (tillSessionId is not long sessionId)
        {
            return Result.Failure(Error.Rule("Res.TillSessionRequired", "Cash for a bill is taken into a till: open a till session and bill from it."));
        }

        Core.Pos.TillSession? session = await _db.Set<Core.Pos.TillSession>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Result.Failure(Error.Rule("Res.TillSessionNotFound", $"Till session {sessionId} is not a till session of this company."));
        }

        if (terminalId is long terminal && session.TerminalId != terminal)
        {
            return Result.Failure(Error.Rule("Res.TillSessionOtherTill", $"Till session {sessionId} is a session of another till."));
        }

        if (cashierUserId is long cashier && session.CashierUserId != cashier)
        {
            return Result.Failure(Error.Rule("Res.TillSessionOtherCashier", $"Till session {sessionId} is another cashier's session."));
        }

        return session.Status != Core.Pos.TillSessionStatus.Open
            ? Result.Failure(Error.Rule("Res.TillSessionClosed", $"Till session {sessionId} has been cashed up; take the cash in an open session."))
            : Result.Success();
    }

    // The cash of a reversed bill goes back out of the drawer it went into. While that session is still open the bill simply drops out of its read; once
    // it has been cashed up (its count and Z are signed off) the cash is paid out of an open session of the same till as a refund movement, counted at
    // that session's cash-up, and the bill stays in its own session's figures.
    private async Task<Result<long?>> RefundBillCashAsync(RestaurantBill bill, long? refundTillSessionId, long operatorUserId, long? authorisedByUserId, DateOnly date, string journalNumber,
        CancellationToken ct)
    {
        if (bill.TillSessionId is not long sessionId || bill.CashAmount <= 0m)
        {
            return (long?)null;
        }

        Core.Pos.TillSession? own = await _db.Set<Core.Pos.TillSession>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct).ConfigureAwait(false);
        if (own is null || own.Status == Core.Pos.TillSessionStatus.Open)
        {
            return (long?)null;
        }

        if (refundTillSessionId is not long refundSession)
        {
            return Error.Rule("Res.SessionClosed", $"Till session {sessionId} has been cashed up; pay the bill's cash back out of an open session of the same till.");
        }

        Result open = await OpenTillSessionAsync(bill.CompanyId, refundSession, ct, terminalId: own.TerminalId).ConfigureAwait(false);
        if (open.IsFailure)
        {
            return open.Error.Code == "Res.TillSessionClosed" ? Error.Rule("Res.SessionClosed", open.Error.Message) : open.Error;
        }

        _db.TillCashMovements.Add(new Core.Pos.TillCashMovement(bill.CompanyId, refundSession, Core.Pos.TillCashKind.Refund, bill.CashAmount, bill.CashAccountCode ?? string.Empty,
            $"Reverse bill {bill.Id} ({bill.RevenueJournalNumber})", operatorUserId, authorisedByUserId, date, journalNumber));
        return refundSession;
    }

    // A supervisor's word stands only from someone other than the person asking, holding the restaurant void / reverse right.
    private async Task<Result> SupervisorAsync(long? authorisedByUserId, long requesterUserId, string what, CancellationToken ct)
    {
        if (authorisedByUserId is not long supervisor)
        {
            return Result.Failure(Error.Forbidden("Res.SupervisorRequired", $"{what} needs a supervisor's authority."));
        }

        if (supervisor == requesterUserId)
        {
            return Result.Failure(Error.Forbidden("Res.SupervisorRequired", $"{what} must be authorised by someone else."));
        }

        if (_permissions is null
            || !(await _permissions.ResolveAsync(supervisor, ct).ConfigureAwait(false)).Has("Pos.Restaurant", Core.Security.OperationFlags.VoidReverse))
        {
            return Result.Failure(Error.Forbidden("Res.SupervisorRequired", $"User {supervisor} may not authorise this: it needs the restaurant void / reverse right (Pos.Restaurant)."));
        }

        return Result.Success();
    }

    // Writes off the ingredients of food the kitchen already made (recipe and modifier ingredients) as kitchen wastage.
    private async Task<Result> WriteOffFiredAsync(long companyId, TableOrder order, IEnumerable<(TableOrderLine Line, decimal Quantity)> fired, KitchenWastage? wastage, string reason, CancellationToken ct)
    {
        List<(TableOrderLine Line, decimal Quantity)> made = [.. fired.Where(f => f.Quantity > 0m)];
        if (made.Count == 0)
        {
            return Result.Success();
        }

        List<long> menuIds = [.. made.Select(m => m.Line.MenuItemId).Distinct()];
        Dictionary<long, IReadOnlyList<RecipeLine>> recipes = (await _db.Set<MenuItem>().Include(m => m.Recipe)
            .Where(m => m.CompanyId == companyId && menuIds.Contains(m.Id)).ToListAsync(ct).ConfigureAwait(false)).ToDictionary(m => m.Id, m => m.Recipe);
        List<(TableOrderLine Line, long ItemId, decimal Quantity)> ingredients = [];
        foreach ((TableOrderLine line, decimal quantity) in made)
        {
            ingredients.AddRange((recipes.GetValueOrDefault(line.MenuItemId) ?? []).Select(r => (line, r.IngredientItemId, r.Quantity * quantity)));
            ingredients.AddRange(line.Modifiers.Where(m => m.IngredientItemId is not null && m.IngredientQuantity > 0m).Select(m => (line, m.IngredientItemId!.Value, m.IngredientQuantity * quantity)));
        }

        // A dish with no stocked ingredients has nothing to write off.
        if (ingredients.Count == 0)
        {
            return Result.Success();
        }

        if (wastage is not KitchenWastage where || where.WarehouseId <= 0 || string.IsNullOrWhiteSpace(where.WastageAccountCode))
        {
            return Result.Failure(Error.Validation("Res.WastageRequired", "Food already fired to the kitchen is written off as wastage: give the kitchen warehouse and the wastage account."));
        }

        foreach ((TableOrderLine line, long itemId, decimal itemQuantity) in ingredients)
        {
            Result<decimal> wasted = await RecordWastageCoreAsync(companyId, itemId, where.WarehouseId, where.Date, itemQuantity, where.WastageAccountCode.Trim(),
                $"{order.OrderNumber} {line.Description}: {reason.Trim()}", allowNegative: true, ct).ConfigureAwait(false);
            if (wasted.IsFailure)
            {
                return Result.Failure(wasted.Error);
            }
        }

        return Result.Success();
    }

    private async Task<Result<TableOrder>> LoadOpenOrderAsync(long companyId, long orderId, CancellationToken ct)
    {
        TableOrder? order = await _db.Set<TableOrder>().Include(o => o.Lines).ThenInclude(l => l.Modifiers).FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Res.OrderNotFound", $"Order {orderId} not found.");
        }

        if (order.Status != TableOrderStatus.Open)
        {
            return Error.Rule("Res.OrderNotOpen", "Only an open order can be billed.");
        }

        if (order.Lines.Count == 0)
        {
            return Error.Rule("Res.EmptyOrder", "An empty order cannot be billed.");
        }

        return order;
    }

    private async Task<Result<BillResult>> PostBillAsync(long companyId, TableOrder order, long warehouseId, DateOnly date, List<(TableOrderLine Line, decimal Quantity)> portions, string cashAccountCode, string? taxAccountCode, decimal serviceChargeAmount, string? serviceChargeAccountCode, decimal tipAmount, string? tipsPayableAccountCode, decimal discountAmount, string? discountAccountCode, CancellationToken ct, IReadOnlyList<TenderLine>? tenders = null,
        long? tillSessionId = null, bool cashIntoTill = false)
    {
        if (serviceChargeAmount < 0m || tipAmount < 0m || discountAmount < 0m)
        {
            return Error.Rule("Res.InvalidExtra", "The service charge, tip and discount cannot be negative.");
        }

        if (serviceChargeAmount > 0m && string.IsNullOrWhiteSpace(serviceChargeAccountCode))
        {
            return Error.Validation("Res.ServiceChargeAccountRequired", "A service charge requires an income account.");
        }

        if (tipAmount > 0m && string.IsNullOrWhiteSpace(tipsPayableAccountCode))
        {
            return Error.Validation("Res.TipsAccountRequired", "A tip requires a tips-payable account.");
        }

        if (discountAmount > 0m && string.IsNullOrWhiteSpace(discountAccountCode))
        {
            return Error.Validation("Res.DiscountAccountRequired", "A discount requires a discounts-allowed account.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Res.CompanyNotFound", $"Company {companyId} not found.");
        }

        // Net and tax for this bill (tax prorated by the billed quantity of each line).
        decimal net = 0m;
        decimal tax = 0m;
        Dictionary<string, decimal> netByAccount = new(StringComparer.Ordinal);
        foreach ((TableOrderLine line, decimal quantity) in portions)
        {
            // A comped line is still made (cost of sales below) but earns no revenue.
            if (line.IsComped)
            {
                continue;
            }

            decimal portionNet = decimal.Round(quantity * line.EffectiveUnitPrice, 4, MidpointRounding.ToEven);
            decimal portionTax = line.Quantity == 0m ? 0m : decimal.Round(line.TaxAmount * quantity / line.Quantity, 4, MidpointRounding.ToEven);
            net += portionNet;
            tax += portionTax;
            netByAccount[line.RevenueAccountCode] = netByAccount.GetValueOrDefault(line.RevenueAccountCode) + portionNet;
        }

        decimal chargeableBeforeDiscount = net + tax + serviceChargeAmount + tipAmount;
        if (discountAmount > chargeableBeforeDiscount)
        {
            return Error.Rule("Res.DiscountTooLarge", $"The discount {discountAmount} exceeds the bill total {chargeableBeforeDiscount}.");
        }

        decimal gross = chargeableBeforeDiscount - discountAmount;

        if (tenders is not null && decimal.Round(tenders.Sum(t => t.Amount), 4, MidpointRounding.ToEven) != decimal.Round(gross, 4, MidpointRounding.ToEven))
        {
            return Error.Rule("Res.TendersMismatch", $"The tenders ({tenders.Sum(t => t.Amount):0.00}) do not sum to the bill gross ({gross:0.00}).");
        }

        // Cash taken for a bill goes into an open till session's drawer (like a till sale), so the X/Z read and the cash-up count it.
        decimal cashTaken = !cashIntoTill ? 0m : tenders is null ? Math.Max(gross, 0m) : tenders.Where(t => t.Type == Core.Pos.TenderType.Cash).Sum(t => t.Amount);
        if (cashTaken > 0m)
        {
            Result tillOpen = await OpenTillSessionAsync(companyId, tillSessionId, ct).ConfigureAwait(false);
            if (tillOpen.IsFailure)
            {
                return tillOpen.Error;
            }
        }

        // Post the guest's charge only when there is one (a fully-comped/-discounted bill charges nothing but still costs).
        string revenueJournal = string.Empty;
        List<JournalLineRequest> postedLines = [];
        if (chargeableBeforeDiscount > 0m)
        {
            // Dr tenders (net of discount) + Dr discounts allowed; Cr revenue (full net), output tax, service charge and tips.
            List<JournalLineRequest> lines = [];
            if (tenders is not null)
            {
                lines.AddRange(tenders.Where(t => t.Amount != 0m).Select(t => new JournalLineRequest { AccountCode = t.AccountCode, Side = JournalSide.Debit, TransactionAmount = t.Amount }));
            }
            else if (gross > 0m)
            {
                lines.Add(new JournalLineRequest { AccountCode = cashAccountCode, Side = JournalSide.Debit, TransactionAmount = gross });
            }

            if (discountAmount > 0m)
            {
                lines.Add(new JournalLineRequest { AccountCode = discountAccountCode!, Side = JournalSide.Debit, TransactionAmount = discountAmount });
            }

            lines.AddRange(netByAccount.Select(kv => new JournalLineRequest { AccountCode = kv.Key, Side = JournalSide.Credit, TransactionAmount = kv.Value }));
            if (tax != 0m && !string.IsNullOrWhiteSpace(taxAccountCode))
            {
                lines.Add(new JournalLineRequest { AccountCode = taxAccountCode!, Side = JournalSide.Credit, TransactionAmount = tax });
            }

            if (serviceChargeAmount > 0m)
            {
                lines.Add(new JournalLineRequest { AccountCode = serviceChargeAccountCode!, Side = JournalSide.Credit, TransactionAmount = serviceChargeAmount });
            }

            if (tipAmount > 0m)
            {
                lines.Add(new JournalLineRequest { AccountCode = tipsPayableAccountCode!, Side = JournalSide.Credit, TransactionAmount = tipAmount });
            }

            Result<PostingReceipt> revenue = await _posting.PostAsync(new JournalRequest
            {
                CompanyId = companyId,
                SourceModule = "RES",
                PostingDate = date,
                TransactionCurrency = company.BaseCurrency,
                DocumentType = "RES-BILL",
                DocumentNumber = $"{order.OrderNumber}-{order.Lines.Sum(l => l.BilledQuantity):0.##}",
                Narrative = $"Restaurant bill {order.OrderNumber}",
                Lines = lines,
            }, ct).ConfigureAwait(false);

            if (revenue.IsFailure)
            {
                return revenue.Error;
            }

            revenueJournal = revenue.Value.JournalNumber;
            postedLines = lines;
        }

        // Cost of sales: explode each billed portion's recipe (and modifiers) into stock issues.
        Result<(decimal Cost, List<long> MovementIds)> cost = await IssueCostOfSalesAsync(companyId, order, warehouseId, date, portions, ct).ConfigureAwait(false);
        if (cost.IsFailure)
        {
            return cost.Error;
        }

        bool fullyBilled = await CloseIfFullyBilledAsync(companyId, order, revenueJournal, ct).ConfigureAwait(false);
        RestaurantBill billRecord = new(companyId, order.Id, date, net, tax, serviceChargeAmount, tipAmount, discountAmount, gross, cost.Value.Cost, revenueJournal, tenderReversible: true);
        foreach (JournalLineRequest jl in postedLines)
        {
            billRecord.AddPosting(jl.AccountCode, jl.Side == JournalSide.Debit, jl.TransactionAmount);
        }

        foreach (long movementId in cost.Value.MovementIds)
        {
            billRecord.AddIssue(movementId);
        }

        if (cashTaken > 0m)
        {
            billRecord.TakeIntoTill(tillSessionId!.Value, cashTaken, tenders is null ? cashAccountCode : tenders.First(t => t.Type == Core.Pos.TenderType.Cash && t.Amount > 0m).AccountCode);
        }

        _db.Set<RestaurantBill>().Add(billRecord);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new BillResult(net, tax, serviceChargeAmount, tipAmount, discountAmount, gross, cost.Value.Cost, fullyBilled, revenueJournal);
    }

    /// <summary>Explodes each billed portion's recipe and modifier ingredients into stock issues, billing the lines.</summary>
    private async Task<Result<(decimal Cost, List<long> MovementIds)>> IssueCostOfSalesAsync(long companyId, TableOrder order, long warehouseId, DateOnly date, List<(TableOrderLine Line, decimal Quantity)> portions, CancellationToken ct)
    {
        List<long> menuIds = portions.Select(p => p.Line.MenuItemId).Distinct().ToList();
        List<MenuItem> menus = await _db.Set<MenuItem>().Include(m => m.Recipe)
            .Where(m => m.CompanyId == companyId && menuIds.Contains(m.Id))
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, IReadOnlyList<RecipeLine>> recipes = menus.ToDictionary(m => m.Id, m => m.Recipe);

        decimal costOfSales = 0m;
        List<long> movementIds = [];
        foreach ((TableOrderLine line, decimal quantity) in portions)
        {
            if (recipes.TryGetValue(line.MenuItemId, out IReadOnlyList<RecipeLine>? recipe))
            {
                foreach (RecipeLine ingredient in recipe)
                {
                    Result<StockMovementResult> issue = await IssueAsync(companyId, ingredient.IngredientItemId, warehouseId, date, ingredient.Quantity * quantity, order.OrderNumber, ct).ConfigureAwait(false);
                    if (issue.IsFailure)
                    {
                        return issue.Error;
                    }

                    costOfSales += issue.Value.Value;
                    movementIds.Add(issue.Value.MovementId);
                }
            }

            // Modifier ingredients (e.g. extra cheese) consume additional stock.
            foreach (OrderLineModifier modifier in line.Modifiers)
            {
                if (modifier.IngredientItemId is not { } modifierItemId || modifier.IngredientQuantity <= 0m)
                {
                    continue;
                }

                Result<StockMovementResult> issue = await IssueAsync(companyId, modifierItemId, warehouseId, date, modifier.IngredientQuantity * quantity, order.OrderNumber, ct).ConfigureAwait(false);
                if (issue.IsFailure)
                {
                    return issue.Error;
                }

                costOfSales += issue.Value.Value;
                movementIds.Add(issue.Value.MovementId);
            }

            line.Bill(quantity);
        }

        return (costOfSales, movementIds);
    }

    private async Task<Result<StockMovementResult>> IssueAsync(long companyId, long itemId, long warehouseId, DateOnly date, decimal quantity, string reference, CancellationToken ct, bool allowNegative = true)
        => await _inventory.IssueAsync(new IssueStockRequest
        {
            CompanyId = companyId,
            ItemId = itemId,
            WarehouseId = warehouseId,
            Date = date,
            Quantity = quantity,
            Reference = reference,
            AllowNegative = allowNegative, // billing COGS = food already served; wastage passes false
            NotVoteControlled = allowNegative, // the cost of food already served is not discretionary spending; wastage stays under the vote
        }, ct).ConfigureAwait(false);

    /// <summary>Marks the order billed and frees its table (if any) when everything billable has been billed.</summary>
    private async Task<bool> CloseIfFullyBilledAsync(long companyId, TableOrder order, string revenueJournal, CancellationToken ct)
    {
        if (!order.IsFullyBilled)
        {
            return false;
        }

        order.MarkBilled(revenueJournal);
        if (order.TableId is { } tableId)
        {
            RestaurantTable? table = await _db.Set<RestaurantTable>().FirstOrDefaultAsync(t => t.Id == tableId, ct).ConfigureAwait(false);
            await FreeTableAndGroupAsync(companyId, table, ct).ConfigureAwait(false);
        }

        return true;
    }

    /// <inheritdoc />
    public Task<Result> VoidLineAsync(long companyId, long orderId, long lineId, string reason, long? authorizedByUserId = null, KitchenWastage? wastage = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => MarkLineAsync(companyId, orderId, lineId, reason, comp: false, authorizedByUserId, wastage, ct), ct);

    /// <inheritdoc />
    public async Task<Result> CompLineAsync(long companyId, long orderId, long lineId, string reason, long? authorizedByUserId = null, CancellationToken ct = default)
        => await MarkLineAsync(companyId, orderId, lineId, reason, comp: true, authorizedByUserId, null, ct).ConfigureAwait(false);

    // A void of a line the kitchen has already made is a loss, not a correction: a supervisor authorises it and its ingredients are written off as wastage (one unit of work).
    private async Task<Result> MarkLineAsync(long companyId, long orderId, long lineId, string reason, bool comp, long? authorizedByUserId, KitchenWastage? wastage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Res.ReasonRequired", $"A {(comp ? "comp" : "void")} requires a reason."));
        }

        TableOrder? order = await _db.Set<TableOrder>().Include(o => o.Lines).ThenInclude(l => l.Modifiers).FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Res.OrderNotFound", $"Order {orderId} not found."));
        }

        if (order.Status != TableOrderStatus.Open)
        {
            return Result.Failure(Error.Rule("Res.OrderNotOpen", $"Only a line on an open order can be {(comp ? "comped" : "voided")}."));
        }

        TableOrderLine? line = order.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
        {
            return Result.Failure(Error.NotFound("Res.LineNotFound", $"Order line {lineId} is not on this order."));
        }

        if (line.BilledQuantity > 0m)
        {
            return Result.Failure(Error.Rule("Res.LineAlreadyBilled", "A line that has already been billed cannot be voided or comped."));
        }

        if (line.IsVoided || line.IsComped)
        {
            return Result.Failure(Error.Rule("Res.LineAlreadyMarked", "The line is already voided or comped."));
        }

        if (comp)
        {
            line.Comp(reason, authorizedByUserId);
        }
        else
        {
            if (line.Fired)
            {
                Result authorised = await SupervisorAsync(authorizedByUserId, order.WaiterUserId, $"Voiding '{line.Description}' (already fired to the kitchen)", ct).ConfigureAwait(false);
                if (authorised.IsFailure)
                {
                    return authorised;
                }

                Result written = await WriteOffFiredAsync(companyId, order, [(line, line.RemainingQuantity)], wastage, reason, ct).ConfigureAwait(false);
                if (written.IsFailure)
                {
                    return written;
                }
            }

            line.Void(reason, authorizedByUserId);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result> CancelOrderAsync(long companyId, long orderId, string reason, long? authorisedByUserId = null, KitchenWastage? wastage = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => CancelOrderCoreAsync(companyId, orderId, reason, authorisedByUserId, wastage, ct), ct);

    // One unit of work: the fired food's wastage and the cancelled order commit together or not at all.
    private async Task<Result> CancelOrderCoreAsync(long companyId, long orderId, string reason, long? authorisedByUserId, KitchenWastage? wastage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Res.ReasonRequired", "Cancelling an order requires a reason."));
        }

        TableOrder? order = await _db.Set<TableOrder>().Include(o => o.Lines).ThenInclude(l => l.Modifiers).FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Res.OrderNotFound", $"Order {orderId} not found."));
        }

        if (order.Status != TableOrderStatus.Open)
        {
            return Result.Failure(Error.Rule("Res.OrderNotOpen", "Only an open order can be cancelled."));
        }

        // Food the kitchen has already made and nobody paid for is a loss: a supervisor authorises it and it is written off as wastage.
        List<(TableOrderLine Line, decimal Quantity)> made = [.. order.Lines.Where(l => l.Fired && !l.IsVoided && l.RemainingQuantity > 0m).Select(l => (l, l.RemainingQuantity))];
        if (made.Count > 0)
        {
            Result authorised = await SupervisorAsync(authorisedByUserId, order.WaiterUserId, $"Cancelling order {order.OrderNumber} (food already fired to the kitchen)", ct).ConfigureAwait(false);
            if (authorised.IsFailure)
            {
                return authorised;
            }

            Result written = await WriteOffFiredAsync(companyId, order, made, wastage, reason, ct).ConfigureAwait(false);
            if (written.IsFailure)
            {
                return written;
            }
        }

        order.Cancel(made.Count > 0 ? authorisedByUserId : null);
        if (order.TableId is { } tableId)
        {
            RestaurantTable? table = await _db.Set<RestaurantTable>().FirstOrDefaultAsync(t => t.Id == tableId, ct).ConfigureAwait(false);
            await FreeTableAndGroupAsync(companyId, table, ct).ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> ReassignWaiterAsync(long companyId, long orderId, long newWaiterUserId, CancellationToken ct = default)
    {
        TableOrder? order = await _db.Set<TableOrder>().FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Res.OrderNotFound", $"Order {orderId} not found."));
        }

        if (order.Status != TableOrderStatus.Open)
        {
            return Result.Failure(Error.Rule("Res.OrderNotOpen", "Only an open order can be reassigned."));
        }

        order.Reassign(newWaiterUserId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> TransferOrderAsync(long companyId, long orderId, long newTableId, CancellationToken ct = default)
    {
        TableOrder? order = await _db.Set<TableOrder>().FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Res.OrderNotFound", $"Order {orderId} not found."));
        }

        if (order.Status != TableOrderStatus.Open)
        {
            return Result.Failure(Error.Rule("Res.OrderNotOpen", "Only an open order can be transferred."));
        }

        RestaurantTable? destination = await _db.Set<RestaurantTable>().FirstOrDefaultAsync(t => t.Id == newTableId && t.CompanyId == companyId, ct).ConfigureAwait(false);
        if (destination is null)
        {
            return Result.Failure(Error.NotFound("Res.TableNotFound", $"Table {newTableId} not found."));
        }

        if (destination.Id == order.TableId)
        {
            return Result.Success();
        }

        if (destination.Status != TableStatus.Free)
        {
            return Result.Failure(Error.Rule("Res.TableOccupied", $"Table {destination.TableNumber} is not free."));
        }

        RestaurantTable? oldTable = await _db.Set<RestaurantTable>().FirstOrDefaultAsync(t => t.Id == order.TableId, ct).ConfigureAwait(false);
        await FreeTableAndGroupAsync(companyId, oldTable, ct).ConfigureAwait(false);
        await OccupyTableAndGroupAsync(companyId, destination, ct).ConfigureAwait(false);
        order.MoveToTable(newTableId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> TransferLineAsync(long companyId, long fromOrderId, long lineId, long toOrderId, decimal? quantity = null, CancellationToken ct = default)
    {
        Result<(TableOrder From, TableOrder To)> tabs = await OpenTabsAsync(companyId, fromOrderId, toOrderId, ct).ConfigureAwait(false);
        if (tabs.IsFailure)
        {
            return tabs.Error;
        }

        TableOrderLine? line = tabs.Value.From.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
        {
            return Error.NotFound("Res.LineNotFound", $"Line {lineId} is not on order {fromOrderId}.");
        }

        decimal moving = quantity ?? line.RemainingQuantity;
        if (line.IsVoided || line.IsComped || moving <= 0m || moving > line.RemainingQuantity)
        {
            return Error.Rule("Res.CannotMoveLine", $"Only an unbilled quantity of a live line can move; {line.RemainingQuantity:0.##} of '{line.Description}' is unbilled.");
        }

        TableOrderLine moved = tabs.Value.To.TakeFrom(line, moving);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return moved.Id;
    }

    /// <inheritdoc />
    public async Task<Result<int>> MergeOrdersAsync(long companyId, long fromOrderId, long intoOrderId, CancellationToken ct = default)
    {
        Result<(TableOrder From, TableOrder To)> tabs = await OpenTabsAsync(companyId, fromOrderId, intoOrderId, ct).ConfigureAwait(false);
        if (tabs.IsFailure)
        {
            return tabs.Error;
        }

        (TableOrder from, TableOrder into) = tabs.Value;
        List<TableOrderLine> live = [.. from.Lines.Where(l => !l.IsVoided && !l.IsComped && l.RemainingQuantity > 0m)];
        foreach (TableOrderLine line in live)
        {
            into.TakeFrom(line, line.RemainingQuantity);
        }

        // Nothing billed on it: the merged-away tab closes and its table is free again; a part-billed tab stays to settle its billed part.
        if (from.Lines.All(l => l.BilledQuantity == 0m) && !from.Lines.Any(l => l.Fired && !l.IsVoided && l.RemainingQuantity > 0m))
        {
            from.Cancel();
            if (from.TableId is long tableId && tableId != into.TableId)
            {
                await FreeTableAndGroupAsync(companyId, await _db.Set<RestaurantTable>().FirstOrDefaultAsync(t => t.Id == tableId, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return live.Count;
    }

    // Two different open tabs of the company, with their lines.
    private async Task<Result<(TableOrder From, TableOrder To)>> OpenTabsAsync(long companyId, long fromOrderId, long toOrderId, CancellationToken ct)
    {
        if (fromOrderId == toOrderId)
        {
            return Error.Validation("Res.SameOrder", "Choose two different tabs.");
        }

        List<TableOrder> orders = await _db.Set<TableOrder>().Include(o => o.Lines).ThenInclude(l => l.Modifiers)
            .Where(o => o.CompanyId == companyId && (o.Id == fromOrderId || o.Id == toOrderId)).ToListAsync(ct).ConfigureAwait(false);
        TableOrder? from = orders.FirstOrDefault(o => o.Id == fromOrderId), to = orders.FirstOrDefault(o => o.Id == toOrderId);
        if (from is null || to is null)
        {
            return Error.NotFound("Res.OrderNotFound", $"Order {(from is null ? fromOrderId : toOrderId)} not found.");
        }

        if (from.Status != TableOrderStatus.Open || to.Status != TableOrderStatus.Open)
        {
            return Error.Rule("Res.OrderNotOpen", "Lines move only between open tabs.");
        }

        return (from, to);
    }

    /// <inheritdoc />
    public async Task<Result<int>> RefreshMenuAvailabilityAsync(long companyId, long warehouseId, CancellationToken ct = default)
    {
        List<MenuItem> menu = await _db.Set<MenuItem>().Include(m => m.Recipe)
            .Where(m => m.CompanyId == companyId)
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<long, decimal> onHand = await _db.Set<Core.Inventory.StockBalance>()
            .Where(b => b.CompanyId == companyId && b.WarehouseId == warehouseId)
            .ToDictionaryAsync(b => b.ItemId, b => b.QuantityOnHand, ct).ConfigureAwait(false);

        int changed = 0;
        foreach (MenuItem item in menu)
        {
            // Only recipe-backed items are stock-gated; leave drinks and other recipe-less items alone.
            if (item.Recipe.Count == 0)
            {
                continue;
            }

            bool canMake = item.Recipe.All(r => onHand.GetValueOrDefault(r.IngredientItemId) >= r.Quantity);
            if (item.IsAvailable != canMake)
            {
                item.IsAvailable = canMake;
                changed++;
            }
        }

        if (changed > 0)
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return changed;
    }

    /// <inheritdoc />
    public async Task<Result> SetMenuAvailabilityAsync(long companyId, long menuItemId, bool isAvailable, CancellationToken ct = default)
    {
        MenuItem? menu = await _db.Set<MenuItem>().FirstOrDefaultAsync(m => m.Id == menuItemId && m.CompanyId == companyId, ct).ConfigureAwait(false);
        if (menu is null)
        {
            return Result.Failure(Error.NotFound("Res.MenuItemNotFound", $"Menu item {menuItemId} not found."));
        }

        menu.IsAvailable = isAvailable;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<int>> DistributeTipsAsync(long companyId, DateOnly date, decimal totalAmount, string tipsPayableAccountCode, IReadOnlyList<TipShare> shares, CancellationToken ct = default)
        => _db.AtomicAsync(() => DistributeTipsCoreAsync(companyId, date, totalAmount, tipsPayableAccountCode, shares, ct), ct);

    // One unit of work: the bill's journal(s), stock issues, tender and order updates commit together or not at all.
    private async Task<Result<int>> DistributeTipsCoreAsync(long companyId, DateOnly date, decimal totalAmount, string tipsPayableAccountCode, IReadOnlyList<TipShare> shares, CancellationToken ct)
    {
        if (totalAmount <= 0m)
        {
            return Error.Rule("Res.NonPositiveAmount", "The amount to distribute must be positive.");
        }

        if (shares.Count == 0)
        {
            return Error.Validation("Res.NoShares", "Distributing tips needs at least one staff share.");
        }

        decimal totalWeight = shares.Sum(s => s.Weight);
        if (totalWeight <= 0m)
        {
            return Error.Rule("Res.InvalidWeights", "The share weights must sum to a positive value.");
        }

        decimal allocated = 0m;
        for (int i = 0; i < shares.Count; i++)
        {
            decimal amount = i == shares.Count - 1
                ? totalAmount - allocated // last share absorbs the rounding remainder
                : decimal.Round(totalAmount * shares[i].Weight / totalWeight, 2, MidpointRounding.ToEven);
            allocated += amount;
            _db.Set<TipAllocation>().Add(new TipAllocation(companyId, shares[i].StaffUserId, amount, date, tipsPayableAccountCode));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return shares.Count;
    }

    /// <inheritdoc />
    public Task<Result<string>> PayTipAllocationAsync(long companyId, long allocationId, DateOnly date, string fundingAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => PayTipAllocationCoreAsync(companyId, allocationId, date, fundingAccountCode, ct), ct);

    // One unit of work: the bill's journal(s), stock issues, tender and order updates commit together or not at all.
    private async Task<Result<string>> PayTipAllocationCoreAsync(long companyId, long allocationId, DateOnly date, string fundingAccountCode, CancellationToken ct)
    {
        TipAllocation? allocation = await _db.Set<TipAllocation>().FirstOrDefaultAsync(a => a.Id == allocationId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (allocation is null)
        {
            return Error.NotFound("Res.TipAllocationNotFound", $"Tip allocation {allocationId} not found.");
        }

        if (allocation.Status == TipAllocationStatus.Paid)
        {
            return Error.Rule("Res.TipAlreadyPaid", $"Tip allocation {allocationId} is already paid.");
        }

        Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "RES",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "RES-TIP-PAY",
            Narrative = $"Tip paid to staff #{allocation.StaffUserId}",
            Lines =
            [
                new JournalLineRequest { AccountCode = allocation.TipsPayableAccountCode, Side = JournalSide.Debit, TransactionAmount = allocation.Amount },
                new JournalLineRequest { AccountCode = fundingAccountCode, Side = JournalSide.Credit, TransactionAmount = allocation.Amount },
            ],
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        allocation.MarkPaid(date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return posted.Value.JournalNumber;
    }

    /// <inheritdoc />
    public async Task<Result<long>> FireOrderAsync(long companyId, long orderId, string ticketNumber, DateOnly date, CancellationToken ct = default)
    {
        TableOrder? order = await _db.Set<TableOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Res.OrderNotFound", $"Order {orderId} not found.");
        }

        if (order.Status != TableOrderStatus.Open)
        {
            return Error.Rule("Res.OrderNotOpen", "Only an open order can be fired to the kitchen.");
        }

        List<TableOrderLine> toFire = order.Lines.Where(l => !l.Fired).ToList();
        if (toFire.Count == 0)
        {
            return Error.Rule("Res.NothingToFire", "There are no new lines to fire to the kitchen.");
        }

        return await FireAsync(companyId, orderId, ticketNumber, date, null, toFire, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<long>> FireCourseAsync(long companyId, long orderId, MenuCourse course, string ticketNumber, DateOnly date, CancellationToken ct = default)
    {
        TableOrder? order = await _db.Set<TableOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Res.OrderNotFound", $"Order {orderId} not found.");
        }

        if (order.Status != TableOrderStatus.Open)
        {
            return Error.Rule("Res.OrderNotOpen", "Only an open order can be fired to the kitchen.");
        }

        List<TableOrderLine> toFire = order.Lines.Where(l => !l.Fired && l.Course == course).ToList();
        if (toFire.Count == 0)
        {
            return Error.Rule("Res.NothingToFire", $"There are no unfired {course} lines to fire.");
        }

        return await FireAsync(companyId, orderId, ticketNumber, date, course, toFire, ct).ConfigureAwait(false);
    }

    // Each preparation station gets its own ticket for its lines (the bar its drinks, the grill its steaks); with more than one station the
    // tickets are numbered by station. The first ticket's id is returned.
    private async Task<Result<long>> FireAsync(long companyId, long orderId, string ticketNumber, DateOnly date, MenuCourse? course, List<TableOrderLine> toFire, CancellationToken ct)
    {
        List<long> menuIds = [.. toFire.Select(l => l.MenuItemId).Distinct()];
        Dictionary<long, string?> stations = await _db.Set<MenuItem>().AsNoTracking().Where(m => menuIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.PrepStation, ct).ConfigureAwait(false);
        List<IGrouping<string?, TableOrderLine>> byStation = [.. toFire.GroupBy(l => stations.GetValueOrDefault(l.MenuItemId)).OrderBy(g => g.Key ?? string.Empty, StringComparer.Ordinal)];
        List<KitchenTicket> tickets = [];
        foreach (IGrouping<string?, TableOrderLine> group in byStation)
        {
            string number = byStation.Count == 1 ? ticketNumber : $"{ticketNumber}-{group.Key ?? "KITCHEN"}";
            KitchenTicket ticket = new(companyId, orderId, number, date, course) { Station = group.Key };
            foreach (TableOrderLine line in group)
            {
                ticket.AddLine(line.MenuItemId, line.Description, line.Quantity, line.Note);
                line.MarkFired();
            }

            tickets.Add(ticket);
        }

        _db.Set<KitchenTicket>().AddRange(tickets);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return tickets[0].Id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StationTicket>> StationQueueAsync(long companyId, string? station, CancellationToken ct = default)
    {
        string? only = string.IsNullOrWhiteSpace(station) ? null : station.Trim();
        List<KitchenTicket> tickets = await _db.Set<KitchenTicket>().AsNoTracking().Include(t => t.Lines)
            .Where(t => t.CompanyId == companyId && t.Station == only && t.Status != KitchenTicketStatus.Served)
            .OrderBy(t => t.Id).ToListAsync(ct).ConfigureAwait(false);
        return [.. tickets.Select(t => new StationTicket(t.Id, t.TicketNumber, t.Status, [.. t.Lines.Select(l => (l.Description, l.Quantity))]))];
    }

    /// <inheritdoc />
    public async Task<Result> AdvanceTicketAsync(long companyId, long ticketId, KitchenTicketStatus status, CancellationToken ct = default)
    {
        KitchenTicket? ticket = await _db.Set<KitchenTicket>().FirstOrDefaultAsync(t => t.Id == ticketId && t.CompanyId == companyId, ct).ConfigureAwait(false);
        if (ticket is null)
        {
            return Result.Failure(Error.NotFound("Res.TicketNotFound", $"Kitchen ticket {ticketId} not found."));
        }

        if (!ticket.Advance(status))
        {
            return Result.Failure(Error.Rule("Res.TicketNotAdvanced", $"Ticket {ticket.TicketNumber} cannot move from {ticket.Status} to {status}."));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <summary>The turn-time window within which two bookings on the same table clash.</summary>
    private static readonly TimeSpan TurnTime = TimeSpan.FromMinutes(90);

    /// <inheritdoc />
    public async Task<Result<long>> ReserveTableAsync(long companyId, long tableId, string guestName, int partySize, DateTime reservedFor, string? guestPhone = null, string? notes = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(guestName))
        {
            return Error.Validation("Res.ReservationNoGuest", "A reservation needs a guest name.");
        }

        if (partySize <= 0)
        {
            return Error.Rule("Res.ReservationInvalidParty", "The party size must be positive.");
        }

        RestaurantTable? table = await _db.Set<RestaurantTable>().FirstOrDefaultAsync(t => t.Id == tableId && t.CompanyId == companyId, ct).ConfigureAwait(false);
        if (table is null)
        {
            return Error.NotFound("Res.TableNotFound", $"Table {tableId} not found.");
        }

        if (partySize > table.Seats)
        {
            return Error.Rule("Res.ReservationTooLarge", $"Table {table.TableNumber} seats {table.Seats}; the party of {partySize} is too large.");
        }

        // A table may hold only one live booking within a turn-time window.
        List<TableReservation> live = await _db.Set<TableReservation>()
            .Where(r => r.CompanyId == companyId && r.TableId == tableId && r.Status == ReservationStatus.Booked)
            .ToListAsync(ct).ConfigureAwait(false);
        if (live.Any(r => (r.ReservedFor - reservedFor).Duration() < TurnTime))
        {
            return Error.Rule("Res.ReservationClash", $"Table {table.TableNumber} already has a booking within {TurnTime.TotalMinutes:0} minutes of that time.");
        }

        TableReservation reservation = new(companyId, tableId, guestName, partySize, reservedFor, guestPhone, notes);
        _db.Set<TableReservation>().Add(reservation);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return reservation.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> SeatReservationAsync(long companyId, long reservationId, string orderNumber, long waiterUserId, DateOnly date, CancellationToken ct = default)
    {
        TableReservation? reservation = await _db.Set<TableReservation>().FirstOrDefaultAsync(r => r.Id == reservationId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (reservation is null)
        {
            return Error.NotFound("Res.ReservationNotFound", $"Reservation {reservationId} not found.");
        }

        if (reservation.Status != ReservationStatus.Booked)
        {
            return Error.Rule("Res.ReservationNotBooked", $"Only a booked reservation can be seated (it is {reservation.Status}).");
        }

        Result<long> opened = await OpenOrderAsync(companyId, reservation.TableId, orderNumber, waiterUserId, date, reservation.PartySize, ct).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            return opened.Error;
        }

        reservation.Seat(opened.Value);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return opened.Value;
    }

    /// <inheritdoc />
    public async Task<Result> CancelReservationAsync(long companyId, long reservationId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Res.ReservationNoReason", "Cancelling a reservation needs a reason."));
        }

        TableReservation? reservation = await _db.Set<TableReservation>().FirstOrDefaultAsync(r => r.Id == reservationId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (reservation is null)
        {
            return Result.Failure(Error.NotFound("Res.ReservationNotFound", $"Reservation {reservationId} not found."));
        }

        if (reservation.Status != ReservationStatus.Booked)
        {
            return Result.Failure(Error.Rule("Res.ReservationNotBooked", $"Only a booked reservation can be cancelled (it is {reservation.Status})."));
        }

        reservation.Cancel(reason);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> MarkReservationNoShowAsync(long companyId, long reservationId, CancellationToken ct = default)
    {
        TableReservation? reservation = await _db.Set<TableReservation>().FirstOrDefaultAsync(r => r.Id == reservationId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (reservation is null)
        {
            return Result.Failure(Error.NotFound("Res.ReservationNotFound", $"Reservation {reservationId} not found."));
        }

        if (reservation.Status != ReservationStatus.Booked)
        {
            return Result.Failure(Error.Rule("Res.ReservationNotBooked", $"Only a booked reservation can be marked a no-show (it is {reservation.Status})."));
        }

        reservation.MarkNoShow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> AddToWaitlistAsync(long companyId, string guestName, int partySize, int quotedMinutes, DateTime addedOn, string? guestPhone = null, string? notes = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(guestName))
        {
            return Error.Validation("Res.WaitlistNoGuest", "A waitlist entry needs a guest name.");
        }

        if (partySize <= 0)
        {
            return Error.Rule("Res.WaitlistInvalidParty", "The party size must be positive.");
        }

        if (quotedMinutes < 0)
        {
            return Error.Rule("Res.WaitlistInvalidQuote", "The quoted wait cannot be negative.");
        }

        WaitlistEntry entry = new(companyId, guestName, partySize, quotedMinutes, addedOn, guestPhone, notes);
        _db.Set<WaitlistEntry>().Add(entry);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return entry.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> SeatFromWaitlistAsync(long companyId, long waitlistId, long tableId, string orderNumber, long waiterUserId, DateOnly date, CancellationToken ct = default)
    {
        WaitlistEntry? entry = await _db.Set<WaitlistEntry>().FirstOrDefaultAsync(w => w.Id == waitlistId && w.CompanyId == companyId, ct).ConfigureAwait(false);
        if (entry is null)
        {
            return Error.NotFound("Res.WaitlistNotFound", $"Waitlist entry {waitlistId} not found.");
        }

        if (entry.Status != WaitlistStatus.Waiting)
        {
            return Error.Rule("Res.WaitlistNotWaiting", $"Only a waiting party can be seated (it is {entry.Status}).");
        }

        Result<long> opened = await OpenOrderAsync(companyId, tableId, orderNumber, waiterUserId, date, entry.PartySize, ct).ConfigureAwait(false);
        if (opened.IsFailure)
        {
            return opened.Error;
        }

        entry.Seat(tableId, opened.Value);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return opened.Value;
    }

    /// <inheritdoc />
    public async Task<Result> RemoveFromWaitlistAsync(long companyId, long waitlistId, CancellationToken ct = default)
    {
        WaitlistEntry? entry = await _db.Set<WaitlistEntry>().FirstOrDefaultAsync(w => w.Id == waitlistId && w.CompanyId == companyId, ct).ConfigureAwait(false);
        if (entry is null)
        {
            return Result.Failure(Error.NotFound("Res.WaitlistNotFound", $"Waitlist entry {waitlistId} not found."));
        }

        if (entry.Status != WaitlistStatus.Waiting)
        {
            return Result.Failure(Error.Rule("Res.WaitlistNotWaiting", $"Only a waiting party can leave the queue (it is {entry.Status})."));
        }

        entry.Leave();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> MarkWaitlistNoShowAsync(long companyId, long waitlistId, CancellationToken ct = default)
    {
        WaitlistEntry? entry = await _db.Set<WaitlistEntry>().FirstOrDefaultAsync(w => w.Id == waitlistId && w.CompanyId == companyId, ct).ConfigureAwait(false);
        if (entry is null)
        {
            return Result.Failure(Error.NotFound("Res.WaitlistNotFound", $"Waitlist entry {waitlistId} not found."));
        }

        if (entry.Status != WaitlistStatus.Waiting)
        {
            return Result.Failure(Error.Rule("Res.WaitlistNotWaiting", $"Only a waiting party can be marked a no-show (it is {entry.Status})."));
        }

        entry.MarkNoShow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> CombineTablesAsync(long companyId, IReadOnlyList<long> tableIds, CancellationToken ct = default)
    {
        List<long> distinct = tableIds.Distinct().ToList();
        if (distinct.Count < 2)
        {
            return Error.Rule("Res.CombineTooFew", "Combining tables needs at least two distinct tables.");
        }

        List<RestaurantTable> tables = await _db.Set<RestaurantTable>()
            .Where(t => t.CompanyId == companyId && distinct.Contains(t.Id))
            .ToListAsync(ct).ConfigureAwait(false);
        if (tables.Count != distinct.Count)
        {
            return Error.NotFound("Res.TableNotFound", "One or more tables were not found.");
        }

        if (tables.Any(t => t.Status != TableStatus.Free))
        {
            return Error.Rule("Res.CombineOccupied", "All tables to combine must be free.");
        }

        if (tables.Any(t => t.GroupId is not null))
        {
            return Error.Rule("Res.CombineAlreadyGrouped", "One or more tables are already combined into a group.");
        }

        List<RestaurantTable> ordered = tables.OrderBy(t => t.TableNumber, StringComparer.Ordinal).ToList();
        RestaurantTable primary = ordered[0];
        string label = string.Join("+", ordered.Select(t => t.TableNumber));
        TableGroup group = new(companyId, label, primary.Id, ordered.Sum(t => t.Seats), ordered.Count);
        _db.Set<TableGroup>().Add(group);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        foreach (RestaurantTable table in ordered)
        {
            table.JoinGroup(group.Id);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return group.Id;
    }

    /// <inheritdoc />
    public async Task<Result> UncombineTablesAsync(long companyId, long groupId, CancellationToken ct = default)
    {
        TableGroup? group = await _db.Set<TableGroup>().FirstOrDefaultAsync(g => g.Id == groupId && g.CompanyId == companyId, ct).ConfigureAwait(false);
        if (group is null)
        {
            return Result.Failure(Error.NotFound("Res.GroupNotFound", $"Table group {groupId} not found."));
        }

        if (group.Status != TableGroupStatus.Active)
        {
            return Result.Failure(Error.Rule("Res.GroupNotActive", "Only an active group can be released."));
        }

        List<RestaurantTable> members = await _db.Set<RestaurantTable>()
            .Where(t => t.CompanyId == companyId && t.GroupId == groupId)
            .ToListAsync(ct).ConfigureAwait(false);
        if (members.Any(t => t.Status != TableStatus.Free))
        {
            return Result.Failure(Error.Rule("Res.GroupSeated", "The group is seated; bill or cancel the order before releasing it."));
        }

        foreach (RestaurantTable table in members)
        {
            table.LeaveGroup();
        }

        group.Release();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateSectionAsync(long companyId, string code, string name, string? description = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Error.Validation("Res.SectionNoCode", "A floor section needs a code.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("Res.SectionNoName", "A floor section needs a name.");
        }

        string normalized = code.Trim().ToUpperInvariant();
        bool exists = await _db.Set<FloorSection>().AnyAsync(s => s.CompanyId == companyId && s.Code == normalized, ct).ConfigureAwait(false);
        if (exists)
        {
            return Error.Rule("Res.SectionDuplicate", $"A floor section with code {normalized} already exists.");
        }

        FloorSection section = new(companyId, code, name, description);
        _db.Set<FloorSection>().Add(section);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return section.Id;
    }

    /// <inheritdoc />
    public async Task<Result> AssignTableToSectionAsync(long companyId, long tableId, long? sectionId, CancellationToken ct = default)
    {
        RestaurantTable? table = await _db.Set<RestaurantTable>().FirstOrDefaultAsync(t => t.Id == tableId && t.CompanyId == companyId, ct).ConfigureAwait(false);
        if (table is null)
        {
            return Result.Failure(Error.NotFound("Res.TableNotFound", $"Table {tableId} not found."));
        }

        if (sectionId is { } id)
        {
            bool sectionExists = await _db.Set<FloorSection>().AnyAsync(s => s.Id == id && s.CompanyId == companyId, ct).ConfigureAwait(false);
            if (!sectionExists)
            {
                return Result.Failure(Error.NotFound("Res.SectionNotFound", $"Floor section {id} not found."));
            }
        }

        table.AssignToSection(sectionId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> AssignSectionServerAsync(long companyId, long sectionId, long? waiterUserId, CancellationToken ct = default)
    {
        FloorSection? section = await _db.Set<FloorSection>().FirstOrDefaultAsync(s => s.Id == sectionId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (section is null)
        {
            return Result.Failure(Error.NotFound("Res.SectionNotFound", $"Floor section {sectionId} not found."));
        }

        section.AssignServer(waiterUserId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetSectionActiveAsync(long companyId, long sectionId, bool isActive, CancellationToken ct = default)
    {
        FloorSection? section = await _db.Set<FloorSection>().FirstOrDefaultAsync(s => s.Id == sectionId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (section is null)
        {
            return Result.Failure(Error.NotFound("Res.SectionNotFound", $"Floor section {sectionId} not found."));
        }

        section.SetActive(isActive);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetAutoGratuityPolicyAsync(long companyId, int minPartySize, decimal percent, string serviceChargeAccountCode, CancellationToken ct = default)
    {
        if (minPartySize > 0 && string.IsNullOrWhiteSpace(serviceChargeAccountCode))
        {
            return Result.Failure(Error.Validation("Res.GratuityAccountRequired", "Auto-gratuity requires a service-charge income account."));
        }

        RestaurantPolicy? policy = await _db.Set<RestaurantPolicy>().FirstOrDefaultAsync(p => p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (policy is null)
        {
            _db.Set<RestaurantPolicy>().Add(new RestaurantPolicy(companyId, minPartySize, percent, serviceChargeAccountCode));
        }
        else
        {
            policy.Set(minPartySize, percent, serviceChargeAccountCode);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<AutoGratuityQuote>> QuoteAutoGratuityAsync(long companyId, long orderId, CancellationToken ct = default)
    {
        TableOrder? order = await _db.Set<TableOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Res.OrderNotFound", $"Order {orderId} not found.");
        }

        RestaurantPolicy? policy = await _db.Set<RestaurantPolicy>().FirstOrDefaultAsync(p => p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (policy is null || policy.AutoGratuityMinParty <= 0 || policy.AutoGratuityPercent <= 0m || order.PartySize < policy.AutoGratuityMinParty)
        {
            return new AutoGratuityQuote(false, 0m, null);
        }

        // Gratuity on the net of the still-billable, non-voided, non-comped lines.
        decimal net = order.Lines
            .Where(l => !l.IsVoided && !l.IsComped && l.RemainingQuantity > 0m)
            .Sum(l => decimal.Round(l.RemainingQuantity * l.UnitPrice, 4, MidpointRounding.ToEven));
        if (net <= 0m)
        {
            return new AutoGratuityQuote(false, 0m, null);
        }

        decimal amount = decimal.Round(net * policy.AutoGratuityPercent / 100m, 2, MidpointRounding.ToEven);
        return new AutoGratuityQuote(amount > 0m, amount, policy.ServiceChargeAccountCode);
    }

    /// <summary>Occupies a table and, when it is combined, every table in its group.</summary>
    private async Task OccupyTableAndGroupAsync(long companyId, RestaurantTable table, CancellationToken ct)
    {
        table.Occupy();
        if (table.GroupId is { } groupId)
        {
            List<RestaurantTable> members = await _db.Set<RestaurantTable>()
                .Where(t => t.CompanyId == companyId && t.GroupId == groupId && t.Id != table.Id)
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (RestaurantTable member in members)
            {
                member.Occupy();
            }
        }
    }

    /// <summary>Frees a table and, when it is combined, every table in its group.</summary>
    private async Task FreeTableAndGroupAsync(long companyId, RestaurantTable? table, CancellationToken ct)
    {
        if (table is null)
        {
            return;
        }

        table.Free();
        if (table.GroupId is { } groupId)
        {
            List<RestaurantTable> members = await _db.Set<RestaurantTable>()
                .Where(t => t.CompanyId == companyId && t.GroupId == groupId && t.Id != table.Id)
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (RestaurantTable member in members)
            {
                member.Free();
            }
        }
    }
}
