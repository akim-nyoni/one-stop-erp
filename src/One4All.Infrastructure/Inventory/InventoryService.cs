using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

        await using IDbContextTransaction? transaction = await BeginIfNoneAsync(ct).ConfigureAwait(false);
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
        await CommitAsync(transaction, ct).ConfigureAwait(false);

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
        if (!request.AllowNegative && balance.QuantityOnHand < request.Quantity)
        {
            return Error.Rule("Inv.InsufficientStock", $"Item '{item.Code}' has {balance.QuantityOnHand} on hand, less than the {request.Quantity} to issue.");
        }

        // Work out the cost without touching the balance: it changes only once the journal has posted, so a refused
        // posting (a closed period, say) leaves nothing behind for a later save to persist.
        decimal averageCost = balance.AverageCost;
        decimal cost = balance.CostOfIssue(request.Quantity);

        await using IDbContextTransaction? transaction = await BeginIfNoneAsync(ct).ConfigureAwait(false);
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

        balance.Issue(request.Quantity);
        StockMovement movement = new(request.CompanyId, request.ItemId, request.WarehouseId, request.Date, StockMovementType.Issue, -request.Quantity, averageCost, -cost, request.Reference)
        {
            JournalEntryId = posting.Value.JournalId,
        };
        _db.Set<StockMovement>().Add(movement);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await CommitAsync(transaction, ct).ConfigureAwait(false);

        return new StockMovementResult(movement.Id, posting.Value.JournalNumber, cost, balance.AverageCost);
    }

    /// <inheritdoc />
    public async Task<Result<StockMovementResult>> ReverseIssueAsync(long companyId, long movementId, DateOnly date, string? reference = null, CancellationToken ct = default)
    {
        StockMovement? original = await _db.Set<StockMovement>().FirstOrDefaultAsync(m => m.Id == movementId && m.CompanyId == companyId, ct).ConfigureAwait(false);
        if (original is null)
        {
            return Error.NotFound("Inv.MovementNotFound", $"Stock movement {movementId} not found.");
        }

        if (original.Type != StockMovementType.Issue)
        {
            return Error.Rule("Inv.NotAnIssue", "Only a stock issue can be reversed with ReverseIssueAsync.");
        }

        if (original.IsReversed)
        {
            return Error.Rule("Inv.AlreadyReversed", $"Stock movement {movementId} has already been reversed.");
        }

        Result<(Item item, string stockCode, CurrencyCode currency)> setup = await SetupAsync(companyId, original.ItemId, ct).ConfigureAwait(false);
        if (setup.IsFailure)
        {
            return setup.Error;
        }

        (Item item, string stockCode, CurrencyCode currency) = setup.Value;
        if (string.IsNullOrEmpty(item.CostOfSalesAccountCode))
        {
            return Error.Rule("Inv.NoCogsAccount", $"Item '{item.Code}' has no cost-of-sales account.");
        }

        decimal returnQty = -original.Quantity;   // the issue stored a negative quantity
        decimal returnValue = -original.Value;     // and a negative value
        decimal unitCost = original.UnitCost;      // the average cost applied at issue time

        // Inverse of the issue: Dr stock control, Cr cost of sales.
        await using IDbContextTransaction? transaction = await BeginIfNoneAsync(ct).ConfigureAwait(false);
        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "INV",
            PostingDate = date,
            TransactionCurrency = currency,
            EnforceManualJournalRules = false,
            DocumentType = "StockIssueReversal",
            DocumentNumber = reference,
            Narrative = $"Reverse stock issue {item.Code}",
            Lines =
            [
                new JournalLineRequest { AccountCode = stockCode, Side = JournalSide.Debit, TransactionAmount = returnValue },
                new JournalLineRequest { AccountCode = item.CostOfSalesAccountCode!, Side = JournalSide.Credit, TransactionAmount = returnValue },
            ],
        }, ct).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        StockBalance balance = await LoadOrCreateBalanceAsync(companyId, original.ItemId, original.WarehouseId, ct).ConfigureAwait(false);
        balance.Receive(returnQty, unitCost);

        StockMovement reversal = new(companyId, original.ItemId, original.WarehouseId, date, StockMovementType.Reversal, returnQty, unitCost, returnValue, reference ?? $"REV:{movementId}")
        {
            JournalEntryId = posting.Value.JournalId,
        };
        _db.Set<StockMovement>().Add(reversal);
        original.MarkReversed();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await CommitAsync(transaction, ct).ConfigureAwait(false);

        return new StockMovementResult(reversal.Id, posting.Value.JournalNumber, returnValue, balance.AverageCost);
    }

    /// <inheritdoc />
    public async Task<Result<decimal>> TransferAsync(long companyId, long itemId, long fromWarehouseId, long toWarehouseId, DateOnly date, decimal quantity, string? reference = null, CancellationToken ct = default)
    {
        if (quantity <= 0m)
        {
            return Error.Rule("Inv.InvalidQuantity", "The transfer quantity must be positive.");
        }

        if (fromWarehouseId == toWarehouseId)
        {
            return Error.Rule("Inv.SameWarehouse", "The source and destination warehouses must differ.");
        }

        Result<(Item item, string stockCode, CurrencyCode currency)> setup = await SetupAsync(companyId, itemId, ct).ConfigureAwait(false);
        if (setup.IsFailure)
        {
            return setup.Error;
        }

        StockBalance source = await LoadOrCreateBalanceAsync(companyId, itemId, fromWarehouseId, ct).ConfigureAwait(false);
        if (source.QuantityOnHand < quantity)
        {
            return Error.Rule("Inv.InsufficientStock", $"Warehouse holds {source.QuantityOnHand}, less than the {quantity} to transfer.");
        }

        decimal unitCost = source.AverageCost;
        decimal cost = source.Issue(quantity);

        StockBalance destination = await LoadOrCreateBalanceAsync(companyId, itemId, toWarehouseId, ct).ConfigureAwait(false);
        destination.Receive(quantity, unitCost);

        // GL-neutral: both warehouses share one stock-control account, so no journal is posted.
        _db.Set<StockMovement>().Add(new StockMovement(companyId, itemId, fromWarehouseId, date, StockMovementType.Transfer, -quantity, unitCost, -cost, reference));
        _db.Set<StockMovement>().Add(new StockMovement(companyId, itemId, toWarehouseId, date, StockMovementType.Transfer, quantity, unitCost, cost, reference));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return cost;
    }

    /// <inheritdoc />
    public async Task<Result<decimal>> AdjustAsync(long companyId, long itemId, long warehouseId, DateOnly date, decimal countedQuantity, string adjustmentAccountCode, string? reference = null, CancellationToken ct = default)
    {
        if (countedQuantity < 0m)
        {
            return Error.Rule("Inv.NegativeCount", "The counted quantity cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(adjustmentAccountCode))
        {
            return Error.Validation("Inv.NoAdjustmentAccount", "A stocktake adjustment needs an adjustment account.");
        }

        Result<(Item item, string stockCode, CurrencyCode currency)> setup = await SetupAsync(companyId, itemId, ct).ConfigureAwait(false);
        if (setup.IsFailure)
        {
            return setup.Error;
        }

        (Item item, string stockCode, CurrencyCode currency) = setup.Value;
        StockBalance balance = await LoadOrCreateBalanceAsync(companyId, itemId, warehouseId, ct).ConfigureAwait(false);
        decimal variance = countedQuantity - balance.QuantityOnHand;
        if (variance == 0m)
        {
            return 0m;
        }

        decimal unitCost = balance.AverageCost;
        decimal value;
        JournalLineRequest debit;
        JournalLineRequest credit;
        if (variance > 0m)
        {
            // Surplus: increase stock at the current average cost, gain to the adjustment account.
            value = decimal.Round(variance * unitCost, 4, MidpointRounding.ToEven);
            debit = new JournalLineRequest { AccountCode = stockCode, Side = JournalSide.Debit, TransactionAmount = value };
            credit = new JournalLineRequest { AccountCode = adjustmentAccountCode, Side = JournalSide.Credit, TransactionAmount = value };
        }
        else
        {
            // Shortage: reduce stock at the average cost, loss to the adjustment account.
            value = balance.CostOfIssue(-variance);
            debit = new JournalLineRequest { AccountCode = adjustmentAccountCode, Side = JournalSide.Debit, TransactionAmount = value };
            credit = new JournalLineRequest { AccountCode = stockCode, Side = JournalSide.Credit, TransactionAmount = value };
        }

        if (value == 0m)
        {
            // No cost basis (average cost is zero): move the quantity only, no journal.
            ApplyVariance(balance, variance, unitCost);
            _db.Set<StockMovement>().Add(new StockMovement(companyId, itemId, warehouseId, date, StockMovementType.Adjustment, variance, unitCost, 0m, reference));
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return 0m;
        }

        // The balance changes only once the journal has posted (see IssueAsync).
        await using IDbContextTransaction? transaction = await BeginIfNoneAsync(ct).ConfigureAwait(false);
        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "INV",
            PostingDate = date,
            TransactionCurrency = currency,
            EnforceManualJournalRules = false,
            DocumentType = "StockAdjustment",
            DocumentNumber = reference,
            Narrative = $"Stocktake adjustment {item.Code}",
            Lines = [debit, credit],
        }, ct).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        ApplyVariance(balance, variance, unitCost);
        _db.Set<StockMovement>().Add(new StockMovement(companyId, itemId, warehouseId, date, StockMovementType.Adjustment, variance, unitCost, variance > 0m ? value : -value, reference)
        {
            JournalEntryId = posting.Value.JournalId,
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await CommitAsync(transaction, ct).ConfigureAwait(false);

        return variance > 0m ? value : -value;
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

    private static void ApplyVariance(StockBalance balance, decimal variance, decimal unitCost)
    {
        if (variance > 0m)
        {
            balance.Receive(variance, unitCost);
        }
        else
        {
            balance.Issue(-variance);
        }
    }

    // Starts a transaction unless the caller already has one open (a stocktake, a despatch), which the work then joins.
    private async Task<IDbContextTransaction?> BeginIfNoneAsync(CancellationToken ct)
        => _db.Database.CurrentTransaction is null ? await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false) : null;

    private static async Task CommitAsync(IDbContextTransaction? transaction, CancellationToken ct)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
    }
}
