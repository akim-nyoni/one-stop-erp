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
/// moving-average stock issues of its ingredients — everything posts through the central engine.
/// </summary>
public sealed class RestaurantService : IRestaurantService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;
    private readonly IInventoryService _inventory;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    /// <param name="inventory">The inventory service (recipe explosion → stock issues).</param>
    public RestaurantService(One4AllDbContext db, IPostingEngine posting, IInventoryService inventory)
    {
        _db = db;
        _posting = posting;
        _inventory = inventory;
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
    public async Task<Result<long>> OpenOrderAsync(long companyId, long tableId, string orderNumber, long waiterUserId, DateOnly date, CancellationToken ct = default)
    {
        RestaurantTable? table = await _db.Set<RestaurantTable>().FirstOrDefaultAsync(t => t.Id == tableId && t.CompanyId == companyId, ct).ConfigureAwait(false);
        if (table is null)
        {
            return Error.NotFound("Res.TableNotFound", $"Table {tableId} not found.");
        }

        if (table.Status != TableStatus.Free)
        {
            return Error.Rule("Res.TableOccupied", $"Table {table.TableNumber} already has an open order.");
        }

        TableOrder order = new(companyId, tableId, orderNumber, waiterUserId, date);
        _db.Set<TableOrder>().Add(order);
        table.Occupy();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return order.Id;
    }

    /// <inheritdoc />
    public async Task<Result<decimal>> AddItemAsync(long companyId, long orderId, long menuItemId, decimal quantity, decimal taxAmount = 0m, CancellationToken ct = default)
    {
        if (quantity <= 0m)
        {
            return Error.Rule("Res.InvalidQuantity", "The quantity must be positive.");
        }

        TableOrder? order = await _db.Set<TableOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Res.OrderNotFound", $"Order {orderId} not found.");
        }

        if (order.Status != TableOrderStatus.Open)
        {
            return Error.Rule("Res.OrderNotOpen", "Items can only be added to an open order.");
        }

        MenuItem? menu = await _db.Set<MenuItem>().FirstOrDefaultAsync(m => m.Id == menuItemId && m.CompanyId == companyId, ct).ConfigureAwait(false);
        if (menu is null)
        {
            return Error.NotFound("Res.MenuItemNotFound", $"Menu item {menuItemId} not found.");
        }

        if (!menu.IsAvailable)
        {
            return Error.Rule("Res.MenuItemUnavailable", $"Menu item {menu.Code} is not available.");
        }

        TableOrderLine line = order.AddLine(menu.Id, menu.Name, quantity, menu.Price, taxAmount, menu.RevenueAccountCode, menu.CostOfSalesAccountCode);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return line.LineNet;
    }

    /// <inheritdoc />
    public async Task<Result<BillResult>> BillOrderAsync(long companyId, long orderId, long warehouseId, DateOnly date, string cashAccountCode, string? taxAccountCode = null, decimal serviceChargeAmount = 0m, string? serviceChargeAccountCode = null, decimal tipAmount = 0m, string? tipsPayableAccountCode = null, CancellationToken ct = default)
    {
        Result<TableOrder> loaded = await LoadOpenOrderAsync(companyId, orderId, ct).ConfigureAwait(false);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        TableOrder order = loaded.Value;
        // Bill everything still outstanding on the order.
        List<(TableOrderLine Line, decimal Quantity)> portions = order.Lines
            .Where(l => l.RemainingQuantity > 0m)
            .Select(l => (l, l.RemainingQuantity))
            .ToList();
        if (portions.Count == 0)
        {
            return Error.Rule("Res.NothingToBill", "The order has nothing left to bill.");
        }

        return await PostBillAsync(companyId, order, warehouseId, date, portions, cashAccountCode, taxAccountCode, serviceChargeAmount, serviceChargeAccountCode, tipAmount, tipsPayableAccountCode, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<BillResult>> BillPartialAsync(long companyId, long orderId, long warehouseId, DateOnly date, IReadOnlyList<SplitPortion> portions, string cashAccountCode, string? taxAccountCode = null, decimal serviceChargeAmount = 0m, string? serviceChargeAccountCode = null, decimal tipAmount = 0m, string? tipsPayableAccountCode = null, CancellationToken ct = default)
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

            if (portion.Quantity <= 0m || portion.Quantity > line.RemainingQuantity + 0.0001m)
            {
                return Error.Rule("Res.InvalidPortion", $"Cannot bill {portion.Quantity} of '{line.Description}'; {line.RemainingQuantity} remains.");
            }

            resolved.Add((line, portion.Quantity));
        }

        return await PostBillAsync(companyId, order, warehouseId, date, resolved, cashAccountCode, taxAccountCode, serviceChargeAmount, serviceChargeAccountCode, tipAmount, tipsPayableAccountCode, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<string>> PayTipsAsync(long companyId, decimal amount, DateOnly date, string tipsPayableAccountCode, string fundingAccountCode, CancellationToken ct = default)
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

    private async Task<Result<TableOrder>> LoadOpenOrderAsync(long companyId, long orderId, CancellationToken ct)
    {
        TableOrder? order = await _db.Set<TableOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
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

    private async Task<Result<BillResult>> PostBillAsync(long companyId, TableOrder order, long warehouseId, DateOnly date, List<(TableOrderLine Line, decimal Quantity)> portions, string cashAccountCode, string? taxAccountCode, decimal serviceChargeAmount, string? serviceChargeAccountCode, decimal tipAmount, string? tipsPayableAccountCode, CancellationToken ct)
    {
        if (serviceChargeAmount < 0m || tipAmount < 0m)
        {
            return Error.Rule("Res.InvalidExtra", "The service charge and tip cannot be negative.");
        }

        if (serviceChargeAmount > 0m && string.IsNullOrWhiteSpace(serviceChargeAccountCode))
        {
            return Error.Validation("Res.ServiceChargeAccountRequired", "A service charge requires an income account.");
        }

        if (tipAmount > 0m && string.IsNullOrWhiteSpace(tipsPayableAccountCode))
        {
            return Error.Validation("Res.TipsAccountRequired", "A tip requires a tips-payable account.");
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
            decimal portionNet = decimal.Round(quantity * line.UnitPrice, 4, MidpointRounding.ToEven);
            decimal portionTax = line.Quantity == 0m ? 0m : decimal.Round(line.TaxAmount * quantity / line.Quantity, 4, MidpointRounding.ToEven);
            net += portionNet;
            tax += portionTax;
            netByAccount[line.RevenueAccountCode] = netByAccount.GetValueOrDefault(line.RevenueAccountCode) + portionNet;
        }

        decimal gross = net + tax + serviceChargeAmount + tipAmount;

        // Dr cash (gross); Cr revenue (net), output tax, service-charge income and tips payable (a liability).
        List<JournalLineRequest> lines = [new JournalLineRequest { AccountCode = cashAccountCode, Side = JournalSide.Debit, TransactionAmount = gross }];
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

        // Cost of sales: explode each billed portion's recipe into stock issues (moving average).
        List<long> menuIds = portions.Select(p => p.Line.MenuItemId).Distinct().ToList();
        List<MenuItem> menus = await _db.Set<MenuItem>().Include(m => m.Recipe)
            .Where(m => m.CompanyId == companyId && menuIds.Contains(m.Id))
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, IReadOnlyList<RecipeLine>> recipes = menus.ToDictionary(m => m.Id, m => m.Recipe);

        decimal costOfSales = 0m;
        foreach ((TableOrderLine line, decimal quantity) in portions)
        {
            if (recipes.TryGetValue(line.MenuItemId, out IReadOnlyList<RecipeLine>? recipe))
            {
                foreach (RecipeLine ingredient in recipe)
                {
                    Result<StockMovementResult> issue = await _inventory.IssueAsync(new IssueStockRequest
                    {
                        CompanyId = companyId,
                        ItemId = ingredient.IngredientItemId,
                        WarehouseId = warehouseId,
                        Date = date,
                        Quantity = ingredient.Quantity * quantity,
                        Reference = order.OrderNumber,
                    }, ct).ConfigureAwait(false);

                    if (issue.IsFailure)
                    {
                        return issue.Error;
                    }

                    costOfSales += issue.Value.Value;
                }
            }

            line.Bill(quantity);
        }

        bool fullyBilled = order.IsFullyBilled;
        if (fullyBilled)
        {
            order.MarkBilled(revenue.Value.JournalNumber);
            RestaurantTable? table = await _db.Set<RestaurantTable>().FirstOrDefaultAsync(t => t.Id == order.TableId, ct).ConfigureAwait(false);
            table?.Free();
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new BillResult(net, tax, serviceChargeAmount, tipAmount, gross, costOfSales, fullyBilled, revenue.Value.JournalNumber);
    }

    /// <inheritdoc />
    public async Task<Result> CancelOrderAsync(long companyId, long orderId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Res.ReasonRequired", "Cancelling an order requires a reason."));
        }

        TableOrder? order = await _db.Set<TableOrder>().FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Res.OrderNotFound", $"Order {orderId} not found."));
        }

        if (order.Status != TableOrderStatus.Open)
        {
            return Result.Failure(Error.Rule("Res.OrderNotOpen", "Only an open order can be cancelled."));
        }

        order.Cancel();
        RestaurantTable? table = await _db.Set<RestaurantTable>().FirstOrDefaultAsync(t => t.Id == order.TableId, ct).ConfigureAwait(false);
        table?.Free();
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
        oldTable?.Free();
        destination.Occupy();
        order.MoveToTable(newTableId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
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

        KitchenTicket ticket = new(companyId, orderId, ticketNumber, date);
        foreach (TableOrderLine line in toFire)
        {
            ticket.AddLine(line.MenuItemId, line.Description, line.Quantity);
            line.MarkFired();
        }

        _db.Set<KitchenTicket>().Add(ticket);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ticket.Id;
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
}
