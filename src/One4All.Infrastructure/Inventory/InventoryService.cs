using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>
/// Inventory service costing each item by its method, moving weighted average or FIFO (see <see cref="StockCosting"/>),
/// posting through the central engine (brief §4.2/§3.6).
/// </summary>
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
    public Task<Result<StockMovementResult>> ReceiveAsync(ReceiveStockRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => ReceiveCoreAsync(request, ct), ct);

    // One unit of work, retried if another posting changes the same stock balance at the same moment.
    private async Task<Result<StockMovementResult>> ReceiveCoreAsync(ReceiveStockRequest request, CancellationToken ct)
    {
        Result<(Item item, string stockCode, CurrencyCode currency)> setup = await SetupAsync(request.CompanyId, request.ItemId, ct).ConfigureAwait(false);
        if (setup.IsFailure)
        {
            return setup.Error;
        }

        (Item item, string stockCode, CurrencyCode currency) = setup.Value;
        decimal actual = decimal.Round(request.Quantity * request.UnitCost, 4, MidpointRounding.ToEven);

        // A standard-costed item goes into stock at its standard; what it really cost differs by the price variance.
        Result<decimal> standard = StandardUnitCost(item, request.UnitCost);
        if (standard.IsFailure)
        {
            return standard.Error;
        }

        decimal unitCost = standard.Value;
        decimal value = decimal.Round(request.Quantity * unitCost, 4, MidpointRounding.ToEven);
        List<JournalLineRequest> lines =
        [
            new JournalLineRequest { AccountCode = stockCode, Side = JournalSide.Debit, TransactionAmount = value },
            new JournalLineRequest { AccountCode = request.CreditAccountCode, Side = JournalSide.Credit, TransactionAmount = actual },
        ];
        if (actual != value)
        {
            lines.Add(new JournalLineRequest
            {
                AccountCode = item.PriceVarianceAccountCode!, Side = actual > value ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(actual - value),
                Narrative = $"Purchase price variance {item.Code}",
            });
        }

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
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        StockBalance balance = await LoadOrCreateBalanceAsync(request.CompanyId, request.ItemId, request.WarehouseId, ct).ConfigureAwait(false);
        StockCosting.Receive(_db, item, balance, request.Date, request.Reference, request.Quantity, value);

        StockMovement movement = new(request.CompanyId, request.ItemId, request.WarehouseId, request.Date, StockMovementType.Receipt, request.Quantity, unitCost, value, request.Reference)
        {
            JournalEntryId = posting.Value.JournalId,
        };
        _db.Set<StockMovement>().Add(movement);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new StockMovementResult(movement.Id, posting.Value.JournalNumber, value, balance.AverageCost);
    }

    /// <inheritdoc />
    public Task<Result<StockMovementResult>> IssueAsync(IssueStockRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => IssueCoreAsync(request, ct), ct);

    // One unit of work, retried if another posting changes the same stock balance at the same moment.
    private async Task<Result<StockMovementResult>> IssueCoreAsync(IssueStockRequest request, CancellationToken ct)
    {
        Result<(Item item, string stockCode, CurrencyCode currency)> setup = await SetupAsync(request.CompanyId, request.ItemId, ct).ConfigureAwait(false);
        if (setup.IsFailure)
        {
            return setup.Error;
        }

        (Item item, string stockCode, CurrencyCode currency) = setup.Value;
        string? chargeAccount = string.IsNullOrWhiteSpace(request.ChargeAccountCode) ? item.CostOfSalesAccountCode : request.ChargeAccountCode.Trim();
        if (string.IsNullOrEmpty(chargeAccount))
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
        StockIssue issue = await StockCosting.PlanIssueAsync(_db, item, balance, request.Quantity, ct).ConfigureAwait(false);
        decimal cost = issue.Cost;

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
                new JournalLineRequest { AccountCode = chargeAccount, CostCentre = request.CostCentre, Side = JournalSide.Debit, TransactionAmount = cost },
                new JournalLineRequest { AccountCode = stockCode, Side = JournalSide.Credit, TransactionAmount = cost },
            ],
        }, ct).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        issue.Apply();
        StockMovement movement = new(request.CompanyId, request.ItemId, request.WarehouseId, request.Date, StockMovementType.Issue, -request.Quantity, issue.UnitCost, -cost, request.Reference)
        {
            JournalEntryId = posting.Value.JournalId,
        };
        _db.Set<StockMovement>().Add(movement);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new StockMovementResult(movement.Id, posting.Value.JournalNumber, cost, balance.AverageCost);
    }

    /// <inheritdoc />
    public Task<Result<StockMovementResult>> ReverseIssueAsync(long companyId, long movementId, DateOnly date, string? reference = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => ReverseIssueCoreAsync(companyId, movementId, date, reference, ct), ct);

    // One unit of work, retried if another posting changes the same stock balance at the same moment.
    private async Task<Result<StockMovementResult>> ReverseIssueCoreAsync(long companyId, long movementId, DateOnly date, string? reference, CancellationToken ct)
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

        // Credit back whatever the issue charged: cost of sales, or the account and cost centre of an internal issue.
        var charged = await _db.Set<JournalLine>()
            .Where(l => l.JournalEntryId == original.JournalEntryId && l.BaseDebit > 0m)
            .Select(l => new { l.AccountCode, l.CostCentre })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        string? chargeAccount = charged?.AccountCode ?? item.CostOfSalesAccountCode;
        string? chargeCentre = string.IsNullOrEmpty(charged?.CostCentre) ? null : charged.CostCentre;
        if (string.IsNullOrEmpty(chargeAccount))
        {
            return Error.Rule("Inv.NoCogsAccount", $"Item '{item.Code}' has no cost-of-sales account.");
        }

        decimal returnQty = -original.Quantity;   // the issue stored a negative quantity
        decimal returnValue = -original.Value;     // and a negative value
        decimal unitCost = original.UnitCost;      // the unit cost applied at issue time

        // Inverse of the issue: Dr stock control, Cr cost of sales.
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
                new JournalLineRequest { AccountCode = chargeAccount, CostCentre = chargeCentre, Side = JournalSide.Credit, TransactionAmount = returnValue },
            ],
        }, ct).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        StockBalance balance = await LoadOrCreateBalanceAsync(companyId, original.ItemId, original.WarehouseId, ct).ConfigureAwait(false);
        StockCosting.Receive(_db, item, balance, date, reference ?? $"REV:{movementId}", returnQty, returnValue);

        StockMovement reversal = new(companyId, original.ItemId, original.WarehouseId, date, StockMovementType.Reversal, returnQty, unitCost, returnValue, reference ?? $"REV:{movementId}")
        {
            JournalEntryId = posting.Value.JournalId,
        };
        _db.Set<StockMovement>().Add(reversal);
        original.MarkReversed();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

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

        Item item = setup.Value.Item1;
        StockIssue issue = await StockCosting.PlanIssueAsync(_db, item, source, quantity, ct).ConfigureAwait(false);
        decimal unitCost = issue.UnitCost;
        decimal cost = issue.Cost;
        issue.Apply();

        // FIFO layers move with their original dates and costs, so the destination issues them in the same order.
        StockBalance destination = await LoadOrCreateBalanceAsync(companyId, itemId, toWarehouseId, ct).ConfigureAwait(false);
        if (issue.Fifo is null)
        {
            StockCosting.Receive(_db, item, destination, date, reference, quantity, cost);
        }
        else
        {
            foreach (FifoTake take in issue.Fifo.Takes)
            {
                StockCosting.Receive(_db, item, destination, take.Layer.LayerDate, take.Layer.Reference, take.Quantity, take.Cost);
            }
        }

        // GL-neutral: both warehouses share one stock-control account, so no journal is posted.
        _db.Set<StockMovement>().Add(new StockMovement(companyId, itemId, fromWarehouseId, date, StockMovementType.Transfer, -quantity, unitCost, -cost, reference));
        _db.Set<StockMovement>().Add(new StockMovement(companyId, itemId, toWarehouseId, date, StockMovementType.Transfer, quantity, unitCost, cost, reference));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return cost;
    }

    /// <inheritdoc />
    public Task<Result<decimal>> AdjustAsync(long companyId, long itemId, long warehouseId, DateOnly date, decimal countedQuantity, string adjustmentAccountCode, string? reference = null, string? reasonCode = null,
        CancellationToken ct = default)
        => _db.AtomicAsync(() => AdjustCoreAsync(companyId, itemId, warehouseId, date, countedQuantity, adjustmentAccountCode, reference, reasonCode, ct), ct);

    // One unit of work, retried if another posting changes the same stock balance at the same moment.
    private async Task<Result<decimal>> AdjustCoreAsync(long companyId, long itemId, long warehouseId, DateOnly date, decimal countedQuantity, string adjustmentAccountCode, string? reference, string? reasonCode,
        CancellationToken ct)
    {
        reasonCode = string.IsNullOrWhiteSpace(reasonCode) ? null : reasonCode.Trim().ToUpperInvariant();
        if (reasonCode != null && !await _db.StockReasonCodes.AnyAsync(r => r.CompanyId == companyId && r.Code == reasonCode && r.IsActive, ct).ConfigureAwait(false))
        {
            return Error.Validation("Inv.UnknownReason", $"'{reasonCode}' is not an active stock reason code.");
        }

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
        StockIssue? shortage = variance < 0m ? await StockCosting.PlanIssueAsync(_db, item, balance, -variance, ct).ConfigureAwait(false) : null;
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
            // Shortage: reduce stock at its cost (average, or the oldest FIFO layers), loss to the adjustment account.
            value = shortage!.Cost;
            debit = new JournalLineRequest { AccountCode = adjustmentAccountCode, Side = JournalSide.Debit, TransactionAmount = value };
            credit = new JournalLineRequest { AccountCode = stockCode, Side = JournalSide.Credit, TransactionAmount = value };
        }

        if (value == 0m)
        {
            // No cost basis (average cost is zero): move the quantity only, no journal.
            ApplyVariance(item, balance, shortage, date, reference, variance, 0m);
            _db.Set<StockMovement>().Add(new StockMovement(companyId, itemId, warehouseId, date, StockMovementType.Adjustment, variance, unitCost, 0m, reference) { ReasonCode = reasonCode });
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return 0m;
        }

        // The balance changes only once the journal has posted (see IssueAsync).
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

        ApplyVariance(item, balance, shortage, date, reference, variance, value);
        _db.Set<StockMovement>().Add(new StockMovement(companyId, itemId, warehouseId, date, StockMovementType.Adjustment, variance, shortage?.UnitCost ?? unitCost, variance > 0m ? value : -value, reference)
        {
            JournalEntryId = posting.Value.JournalId,
            ReasonCode = reasonCode,
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

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

    /// <inheritdoc />
    public Task<Result<decimal>> RevalueStandardCostAsync(long companyId, long itemId, decimal newStandardCost, DateOnly date, string revaluationAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => RevalueStandardCostCoreAsync(companyId, itemId, newStandardCost, date, revaluationAccountCode, ct), ct);

    // One unit: the journal, every warehouse's balance and the new standard change together.
    private async Task<Result<decimal>> RevalueStandardCostCoreAsync(long companyId, long itemId, decimal newStandardCost, DateOnly date, string revaluationAccountCode, CancellationToken ct)
    {
        if (newStandardCost < 0m || string.IsNullOrWhiteSpace(revaluationAccountCode))
        {
            return Error.Validation("Inv.InvalidRevaluation", "A standard cost revaluation needs a non-negative cost and a revaluation account.");
        }

        Result<(Item item, string stockCode, CurrencyCode currency)> setup = await SetupAsync(companyId, itemId, ct).ConfigureAwait(false);
        if (setup.IsFailure)
        {
            return setup.Error;
        }

        (Item item, string stockCode, CurrencyCode currency) = setup.Value;
        if (item.CostingMethod != CostingMethod.Standard)
        {
            return Error.Rule("Inv.NotStandardCosted", $"Item '{item.Code}' is not standard-costed.");
        }

        List<StockBalance> balances = await _db.Set<StockBalance>()
            .Where(b => b.CompanyId == companyId && b.ItemId == itemId && b.QuantityOnHand > 0m)
            .ToListAsync(ct).ConfigureAwait(false);
        List<(StockBalance Balance, decimal Change)> changes = [.. balances
            .Select(b => (b, decimal.Round(b.QuantityOnHand * newStandardCost, 4, MidpointRounding.ToEven) - b.TotalValue))
            .Where(c => c.Item2 != 0m)];
        decimal net = changes.Sum(c => c.Change);
        Result<PostingReceipt>? posting = null;
        if (net != 0m)
        {
            posting = await _postingEngine.PostAsync(new JournalRequest
            {
                CompanyId = companyId,
                SourceModule = "INV",
                PostingDate = date,
                TransactionCurrency = currency,
                EnforceManualJournalRules = false,
                DocumentType = "StandardCostRevaluation",
                DocumentNumber = item.Code,
                Narrative = $"Standard cost of {item.Code} to {newStandardCost:0.######}",
                Lines =
                [
                    new JournalLineRequest { AccountCode = stockCode, Side = net > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(net) },
                    new JournalLineRequest { AccountCode = revaluationAccountCode.Trim(), Side = net > 0m ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = Math.Abs(net) },
                ],
            }, ct).ConfigureAwait(false);
            if (posting.IsFailure)
            {
                return posting.Error;
            }
        }

        foreach ((StockBalance balance, decimal change) in changes)
        {
            balance.AddValue(change);
            _db.Set<StockMovement>().Add(new StockMovement(companyId, itemId, balance.WarehouseId, date, StockMovementType.StandardRevaluation, 0m, newStandardCost, change, item.Code)
            {
                JournalEntryId = posting?.Value.JournalId,
            });
        }

        item.StandardCost = newStandardCost;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return net;
    }

    // The unit cost stock goes in at: the standard for a standard-costed item (which needs a variance account), else the actual cost.
    private static Result<decimal> StandardUnitCost(Item item, decimal actualUnitCost)
    {
        if (item.CostingMethod != CostingMethod.Standard)
        {
            return actualUnitCost;
        }

        if (item.StandardCost is not decimal standard || string.IsNullOrWhiteSpace(item.PriceVarianceAccountCode))
        {
            return Error.Rule("Inv.NoStandardCost", $"Item '{item.Code}' is standard-costed but has no standard cost or price variance account.");
        }

        return standard;
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

    private void ApplyVariance(Item item, StockBalance balance, StockIssue? shortage, DateOnly date, string? reference, decimal variance, decimal value)
    {
        if (shortage is null)
        {
            StockCosting.Receive(_db, item, balance, date, reference, variance, value);
        }
        else
        {
            shortage.Apply();
        }
    }
}
