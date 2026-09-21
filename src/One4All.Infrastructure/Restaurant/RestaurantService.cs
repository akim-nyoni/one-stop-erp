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
    public async Task<Result<BillResult>> BillOrderAsync(long companyId, long orderId, long warehouseId, DateOnly date, string cashAccountCode, string? taxAccountCode = null, CancellationToken ct = default)
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

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Res.CompanyNotFound", $"Company {companyId} not found.");
        }

        decimal net = order.NetTotal;
        decimal tax = order.TaxTotal;
        decimal gross = order.GrossTotal;

        // Revenue: Dr cash (gross), Cr revenue by account (net), Cr output tax (tax).
        List<JournalLineRequest> lines = [new JournalLineRequest { AccountCode = cashAccountCode, Side = JournalSide.Debit, TransactionAmount = gross }];
        foreach (IGrouping<string, TableOrderLine> revenueGroup in order.Lines.GroupBy(l => l.RevenueAccountCode))
        {
            lines.Add(new JournalLineRequest { AccountCode = revenueGroup.Key, Side = JournalSide.Credit, TransactionAmount = revenueGroup.Sum(l => l.LineNet) });
        }

        if (tax != 0m && !string.IsNullOrWhiteSpace(taxAccountCode))
        {
            lines.Add(new JournalLineRequest { AccountCode = taxAccountCode!, Side = JournalSide.Credit, TransactionAmount = tax });
        }

        Result<PostingReceipt> revenue = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "RES",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "RES-BILL",
            DocumentNumber = order.OrderNumber,
            Narrative = $"Restaurant bill {order.OrderNumber}",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (revenue.IsFailure)
        {
            return revenue.Error;
        }

        // Cost of sales: explode each ordered menu item's recipe into stock issues (moving average).
        List<long> menuIds = order.Lines.Select(l => l.MenuItemId).Distinct().ToList();
        List<MenuItem> menus = await _db.Set<MenuItem>().Include(m => m.Recipe)
            .Where(m => m.CompanyId == companyId && menuIds.Contains(m.Id))
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, IReadOnlyList<RecipeLine>> recipes = menus.ToDictionary(m => m.Id, m => m.Recipe);

        decimal costOfSales = 0m;
        foreach (TableOrderLine orderLine in order.Lines)
        {
            if (!recipes.TryGetValue(orderLine.MenuItemId, out IReadOnlyList<RecipeLine>? recipe))
            {
                continue;
            }

            foreach (RecipeLine ingredient in recipe)
            {
                Result<StockMovementResult> issue = await _inventory.IssueAsync(new IssueStockRequest
                {
                    CompanyId = companyId,
                    ItemId = ingredient.IngredientItemId,
                    WarehouseId = warehouseId,
                    Date = date,
                    Quantity = ingredient.Quantity * orderLine.Quantity,
                    Reference = order.OrderNumber,
                }, ct).ConfigureAwait(false);

                if (issue.IsFailure)
                {
                    return issue.Error;
                }

                costOfSales += issue.Value.Value;
            }
        }

        order.MarkBilled(revenue.Value.JournalNumber);
        RestaurantTable? table = await _db.Set<RestaurantTable>().FirstOrDefaultAsync(t => t.Id == order.TableId, ct).ConfigureAwait(false);
        table?.Free();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new BillResult(net, tax, gross, costOfSales, revenue.Value.JournalNumber);
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
}
