using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>Inventory service with moving weighted-average costing, posting through the central engine (brief §4.2/§3.6).</summary>
public sealed class InventoryService : IInventoryService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _postingEngine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">The posting engine.</param>
    public InventoryService(One4AllDbContext db, IPostingEngine postingEngine)
    {
        _db = db;
        _postingEngine = postingEngine;
    }

    /// <inheritdoc />
    public async Task<Result<StockMovementResult>> ReceiveAsync(ReceiveStockRequest request, CancellationToken ct = default)
    {
        Result<(Item item, string stockCode, CurrencyCode currency)> setup = await SetupAsync(request.CompanyId, request.ItemId, ct).ConfigureAwait(false);
        if (setup.IsFailure)
        {
            return setup.Error;
        }

        (Item item, string stockCode, CurrencyCode currency) = setup.Value;
        decimal value = decimal.Round(request.Quantity * request.UnitCost, 4, MidpointRounding.ToEven);

        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = request.CompanyId,
            SourceModule = "INV",
            PostingDate = request.Date,
            TransactionCurrency = currency,
            EnforceManualJournalRules = false,
            DocumentType = "StockReceipt",
            DocumentNumber = request.Reference,
            Narrative = $"Stock receipt {item.Code}",
            Lines =
            [
                new JournalLineRequest { AccountCode = stockCode, Side = JournalSide.Debit, TransactionAmount = value },
                new JournalLineRequest { AccountCode = request.CreditAccountCode, Side = JournalSide.Credit, TransactionAmount = value },
            ],
        }, ct).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        StockBalance balance = await LoadOrCreateBalanceAsync(request.CompanyId, request.ItemId, request.WarehouseId, ct).ConfigureAwait(false);
        balance.Receive(request.Quantity, request.UnitCost);

        StockMovement movement = new(request.CompanyId, request.ItemId, request.WarehouseId, request.Date, StockMovementType.Receipt, request.Quantity, request.UnitCost, value, request.Reference)
        {
            JournalEntryId = posting.Value.JournalId,
        };
        _db.Set<StockMovement>().Add(movement);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new StockMovementResult(movement.Id, posting.Value.JournalNumber, value, balance.AverageCost);
    }

    /// <inheritdoc />
    public async Task<Result<StockMovementResult>> IssueAsync(IssueStockRequest request, CancellationToken ct = default)
    {
        Result<(Item item, string stockCode, CurrencyCode currency)> setup = await SetupAsync(request.CompanyId, request.ItemId, ct).ConfigureAwait(false);
        if (setup.IsFailure)
        {
            return setup.Error;
        }

        (Item item, string stockCode, CurrencyCode currency) = setup.Value;
        if (string.IsNullOrEmpty(item.CostOfSalesAccountCode))
        {
            return Error.Rule("Inv.NoCogsAccount", $"Item '{item.Code}' has no cost-of-sales account.");
        }

        StockBalance balance = await LoadOrCreateBalanceAsync(request.CompanyId, request.ItemId, request.WarehouseId, ct).ConfigureAwait(false);
        decimal averageCost = balance.AverageCost;
        decimal cost = balance.Issue(request.Quantity);

        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = request.CompanyId,
            SourceModule = "INV",
            PostingDate = request.Date,
            TransactionCurrency = currency,
            EnforceManualJournalRules = false,
            DocumentType = "StockIssue",
            DocumentNumber = request.Reference,
            Narrative = $"Stock issue {item.Code}",
            Lines =
            [
                new JournalLineRequest { AccountCode = item.CostOfSalesAccountCode!, Side = JournalSide.Debit, TransactionAmount = cost },
                new JournalLineRequest { AccountCode = stockCode, Side = JournalSide.Credit, TransactionAmount = cost },
            ],
        }, ct).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        StockMovement movement = new(request.CompanyId, request.ItemId, request.WarehouseId, request.Date, StockMovementType.Issue, -request.Quantity, averageCost, -cost, request.Reference)
        {
            JournalEntryId = posting.Value.JournalId,
        };
        _db.Set<StockMovement>().Add(movement);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new StockMovementResult(movement.Id, posting.Value.JournalNumber, cost, balance.AverageCost);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockValuationLine>> StockValuationAsync(long companyId, long? warehouseId = null, CancellationToken ct = default)
    {
        var query = from bal in _db.Set<StockBalance>()
                    join item in _db.Set<Item>() on bal.ItemId equals item.Id
                    join wh in _db.Set<Warehouse>() on bal.WarehouseId equals wh.Id
                    where bal.CompanyId == companyId && bal.QuantityOnHand != 0m && (warehouseId == null || bal.WarehouseId == warehouseId)
                    orderby item.Code
                    select new { item.Code, item.Description, WarehouseCode = wh.Code, bal.QuantityOnHand, bal.AverageCost, bal.TotalValue };

        var rows = await query.ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(r => new StockValuationLine(r.Code, r.Description, r.WarehouseCode, r.QuantityOnHand, r.AverageCost, r.TotalValue)).ToList();
    }

    private async Task<Result<(Item, string, CurrencyCode)>> SetupAsync(long companyId, long itemId, CancellationToken ct)
    {
        Item? item = await _db.Set<Item>().FirstOrDefaultAsync(i => i.Id == itemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound("Inv.ItemNotFound", $"Item {itemId} not found.");
        }

        string? stockCode = await _db.Accounts
            .Where(a => a.CompanyId == companyId && a.ControlAccountType == ControlAccountType.Stock)
            .Select(a => a.AccountCode).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (stockCode is null)
        {
            return Error.Rule("Inv.NoStockAccount", "No stock control account is configured.");
        }

        Company company = await _db.Companies.FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        return (item, stockCode, company.BaseCurrency);
    }

    private async Task<StockBalance> LoadOrCreateBalanceAsync(long companyId, long itemId, long warehouseId, CancellationToken ct)
    {
        StockBalance? balance = await _db.Set<StockBalance>()
            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.ItemId == itemId && b.WarehouseId == warehouseId, ct)
            .ConfigureAwait(false);

        if (balance is null)
        {
            balance = new StockBalance(companyId, itemId, warehouseId);
            _db.Set<StockBalance>().Add(balance);
        }

        return balance;
    }
}
