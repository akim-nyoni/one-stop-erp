using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Inventory;
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
    private readonly ILotTrackingService? _lots;
    private readonly Application.Abstractions.Security.IPermissionService? _permissions;
    private readonly IPostingEngine _postingEngine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">The posting engine.</param>
    public InventoryService(One4AllDbContext db, IPostingEngine postingEngine, Application.Abstractions.Security.IPermissionService? permissions = null,
        ILotTrackingService? lots = null)
    {
        _lots = lots;
        _permissions = permissions;
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

        // A catch-weight item is weighed in: its weight is required, its pieces' average must sit near the nominal weight, and it may be bought by weight.
        decimal pieceCost = request.UnitCost;
        if (item.IsCatchWeight)
        {
            Error? weighed = CatchWeightCheck(item, request.Quantity, request.Weight);
            if (weighed is not null)
            {
                return weighed;
            }

            if (request.CostPerWeightUnit is decimal perWeight)
            {
                pieceCost = request.Quantity == 0m ? 0m : request.Weight!.Value * perWeight / request.Quantity;
            }
        }

        decimal actual = decimal.Round(request.Quantity * pieceCost, 4, MidpointRounding.ToEven);

        // A standard-costed item goes into stock at its standard; what it really cost differs by the price variance.
        Result<decimal> standard = StandardUnitCost(item, pieceCost);
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
        decimal shortfall = item.CostingMethod == CostingMethod.WeightedAverage && balance.QuantityOnHand < 0m
            && await _db.Companies.AnyAsync(c => c.Id == request.CompanyId && c.NegativeStockControl, ct).ConfigureAwait(false)
            ? Math.Min(-balance.QuantityOnHand, request.Quantity) : 0m;
        decimal costUsed = balance.AverageCost;
        StockCosting.Receive(_db, item, balance, request.Date, request.Reference, request.Quantity, value);
        if (item.IsCatchWeight)
        {
            balance.AdjustWeight(request.Weight!.Value);
        }

        // Goods sold short went out at the old average: the units now received cover them, so their cost is put right (cost of sales against
        // stock) instead of being spread over what is left.
        if (shortfall > 0m)
        {
            decimal correction = decimal.Round(shortfall * (unitCost - costUsed), 4, MidpointRounding.ToEven);
            string correctionJournal = string.Empty;
            if (correction != 0m && !string.IsNullOrEmpty(item.CostOfSalesAccountCode))
            {
                Result<PostingReceipt> corrected = await _postingEngine.PostAsync(new JournalRequest
                {
                    CompanyId = request.CompanyId, SourceModule = "INV", PostingDate = request.Date, TransactionCurrency = currency, EnforceManualJournalRules = false,
                    DocumentType = "NegativeStockCorrection", DocumentNumber = request.Reference, Narrative = $"Cost of {shortfall:0.###} {item.Code} sold short, corrected to the receipt cost",
                    Lines =
                    [
                        new JournalLineRequest { AccountCode = item.CostOfSalesAccountCode, Side = correction > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(correction) },
                        new JournalLineRequest { AccountCode = stockCode, Side = correction > 0m ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = Math.Abs(correction) },
                    ],
                }, ct).ConfigureAwait(false);
                if (corrected.IsFailure)
                {
                    return corrected.Error;
                }

                balance.AddValue(-correction);
                correctionJournal = corrected.Value.JournalNumber;
            }

            foreach (NegativeStockEvent open in await _db.Set<NegativeStockEvent>()
                .Where(e => e.CompanyId == request.CompanyId && e.ItemId == request.ItemId && e.WarehouseId == request.WarehouseId && e.CorrectionJournalNumber == null)
                .ToListAsync(ct).ConfigureAwait(false))
            {
                open.Correct(correctionJournal);
            }
        }

        StockMovement movement = new(request.CompanyId, request.ItemId, request.WarehouseId, request.Date, StockMovementType.Receipt, request.Quantity, unitCost, value, request.Reference)
        {
            JournalEntryId = posting.Value.JournalId,
            Weight = item.IsCatchWeight ? request.Weight : null,
        };
        _db.Set<StockMovement>().Add(movement);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        Result traced = await RecordLotsAsync(request.CompanyId, item, movement, request.Lots, inbound: true, fromTill: false, ct).ConfigureAwait(false);
        if (traced.IsFailure)
        {
            return traced.Error;
        }

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

        // Stock reserved for sales orders is not for anyone else: only the reserving order's despatch may take it.
        if (!request.AllowNegative)
        {
            decimal free = await SalesOrders.StockReservations.FreeAsync(_db, request.CompanyId, request.ItemId, request.WarehouseId, request.SalesOrderLineId, ct, balance.QuantityOnHand)
                .ConfigureAwait(false);
            if (free < request.Quantity)
            {
                return Error.Rule("Inv.Reserved", $"Only {Math.Max(0m, free):0.###} of '{item.Code}' is free here; the rest is reserved for sales orders.");
            }
        }

        // Negative stock by exception: a reason and an authorised supervisor (a till sale is let through), and the event is kept.
        NegativeStockEvent? negative = null;
        decimal shortBy = request.Quantity - Math.Max(0m, balance.QuantityOnHand);
        if (item.IsStockTracked && shortBy > 0m && await _db.Companies.AnyAsync(c => c.Id == request.CompanyId && c.NegativeStockControl, ct).ConfigureAwait(false))
        {
            if (!request.FromTill)
            {
                if (string.IsNullOrWhiteSpace(request.NegativeReason) || request.NegativeAuthorisedByUserId is not long authoriser)
                {
                    return Error.Rule("Inv.NegativeNeedsAuthority", $"Issuing {request.Quantity} of '{item.Code}' with {balance.QuantityOnHand} on hand needs a reason and a supervisor's authority.");
                }

                if (_permissions is null || !(await _permissions.ResolveAsync(authoriser, ct).ConfigureAwait(false)).Has("Inventory.Issue", Core.Security.OperationFlags.AuthoriseLevel1))
                {
                    return Error.Forbidden("Inv.NegativeNotAllowed", "Only a user who may authorise stock issues can let stock go negative.");
                }
            }

            negative = new NegativeStockEvent(request.CompanyId, request.ItemId, request.WarehouseId, request.Date, shortBy, balance.AverageCost, request.Reference,
                request.FromTill ? "Sold at the till with none on hand" : request.NegativeReason!.Trim(), request.FromTill ? null : request.NegativeAuthorisedByUserId, request.FromTill);
        }

        // Work out the cost without touching the balance: it changes only once the journal has posted, so a refused
        // posting (a closed period, say) leaves nothing behind for a later save to persist.
        // Specific identification: the batches or serials that leave (named, or the earliest-expiry ones) carry their own receipt cost.
        IReadOnlyList<LotQuantity> lotsOut = request.Lots;
        StockIssue issue;
        if (item.CostingMethod == CostingMethod.SpecificIdentification && item.Tracking != ItemTracking.None && _lots is not null)
        {
            if (lotsOut.Count == 0)
            {
                (IReadOnlyList<LotQuantity> picks, decimal shortfall) = await _lots.SuggestPicksAsync(request.CompanyId, item.Id, request.WarehouseId, request.Quantity, request.Date, ct)
                    .ConfigureAwait(false);
                if (shortfall > 0m)
                {
                    return Error.Rule("Inv.SpecificLotsShort", $"'{item.Code}' is costed by specific identification and only {request.Quantity - shortfall:0.###} is on hand in known batches.");
                }

                lotsOut = picks;
            }

            if (lotsOut.Sum(l => l.Quantity) != request.Quantity)
            {
                return Error.Validation("Inv.LotsQuantity", $"The batches named come to {lotsOut.Sum(l => l.Quantity):0.###}, not the {request.Quantity:0.###} issued.");
            }

            issue = await StockCosting.PlanSpecificIssueAsync(_db, item, balance, lotsOut, ct).ConfigureAwait(false);
        }
        else if (item.IsCatchWeight)
        {
            // Costed by the weight that leaves, at the stock's cost per weight unit; the last pieces take whatever value and weight are left.
            if (request.Weight is not > 0m)
            {
                return Error.Validation("Inv.WeightRequired", $"'{item.Code}' is catch-weight: weigh what is issued.");
            }

            if (!request.AllowNegative && request.Weight > balance.WeightOnHand && request.Quantity < balance.QuantityOnHand)
            {
                return Error.Rule("Inv.InsufficientWeight", $"'{item.Code}' has {balance.WeightOnHand:0.###} {item.CatchWeightUnit} on hand, less than the {request.Weight:0.###} to issue.");
            }

            decimal weightCost = request.Quantity >= balance.QuantityOnHand && balance.QuantityOnHand > 0m ? balance.TotalValue
                : decimal.Round(request.Weight.Value * balance.CostPerWeight, 4, MidpointRounding.ToEven);
            issue = new StockIssue(balance, request.Quantity, weightCost, null, AtCost: true);
        }
        else
        {
            issue = await StockCosting.PlanIssueAsync(_db, item, balance, request.Quantity, ct).ConfigureAwait(false);
        }

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
            BudgetGatePassed = request.BudgetGatePassed,
            NotVoteControlled = request.NotVoteControlled,
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
        if (negative is not null)
        {
            _db.Set<NegativeStockEvent>().Add(negative);
        }

        decimal? weightOut = null;
        if (item.IsCatchWeight)
        {
            // With the last pieces gone, whatever weight is left goes with them (the scale's difference on the way out).
            weightOut = balance.QuantityOnHand <= 0m ? balance.WeightOnHand : request.Weight!.Value;
            balance.AdjustWeight(-weightOut.Value);
        }

        StockMovement movement = new(request.CompanyId, request.ItemId, request.WarehouseId, request.Date, StockMovementType.Issue, -request.Quantity, issue.UnitCost, -cost, request.Reference)
        {
            JournalEntryId = posting.Value.JournalId,
            Weight = -weightOut,
        };
        _db.Set<StockMovement>().Add(movement);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        Result traced = await RecordLotsAsync(request.CompanyId, item, movement, lotsOut, inbound: false, request.FromTill, ct).ConfigureAwait(false);
        if (traced.IsFailure)
        {
            return traced.Error;
        }

        return new StockMovementResult(movement.Id, posting.Value.JournalNumber, cost, balance.AverageCost);
    }

    // Batches / serials: recorded when given; under the company's enforcement a tracked receipt must carry them and an issue without them
    // takes the earliest-expiry batches on hand (a till sale takes what there is; anything else short is refused).
    private async Task<Result> RecordLotsAsync(long companyId, Item item, StockMovement movement, IReadOnlyList<LotQuantity> lots, bool inbound, bool fromTill, CancellationToken ct)
    {
        if (_lots is null || item.Tracking == ItemTracking.None)
        {
            return Result.Success();
        }

        bool enforced = await _db.Companies.AnyAsync(c => c.Id == companyId && c.EnforceLotTracking, ct).ConfigureAwait(false);
        if (lots.Count == 0)
        {
            if (!enforced)
            {
                return Result.Success();
            }

            if (inbound)
            {
                return Result.Failure(Error.Validation("Inv.LotsRequired", $"Receiving '{item.Code}' needs its {(item.Tracking == ItemTracking.Serial ? "serial numbers" : "batch numbers")}."));
            }

            (IReadOnlyList<LotQuantity> picks, decimal shortfall) = await _lots.SuggestPicksAsync(companyId, item.Id, movement.WarehouseId, Math.Abs(movement.Quantity), movement.Date, ct)
                .ConfigureAwait(false);
            if (shortfall > 0m && !fromTill)
            {
                return Result.Failure(Error.Rule("Inv.LotsShort", $"Only {Math.Abs(movement.Quantity) - shortfall:0.###} of '{item.Code}' is on hand in known batches; name the batches issued."));
            }

            if (picks.Count == 0)
            {
                return Result.Success();
            }

            lots = picks;
            if (shortfall > 0m)
            {
                // A till sale beyond the known batches: the batches there are go, the rest stays untraced (and shows as an exception).
                return await _lots.RecordPartialAsync(companyId, movement.Id, picks, ct).ConfigureAwait(false);
            }
        }

        return await _lots.RecordAsync(companyId, movement.Id, lots, ct).ConfigureAwait(false);
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
        => _db.AtomicAsync(() => AdjustCoreAsync(companyId, itemId, warehouseId, date, countedQuantity, adjustmentAccountCode, reference, reasonCode, null, ct), ct);

    /// <inheritdoc />
    public Task<Result<decimal>> AdjustCountAsync(long companyId, long itemId, long warehouseId, DateOnly date, decimal countedQuantity, decimal snapshotQuantity, string adjustmentAccountCode,
        string? reference = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => AdjustCoreAsync(companyId, itemId, warehouseId, date, countedQuantity, adjustmentAccountCode, reference, null, snapshotQuantity, ct), ct);

    // One unit of work, retried if another posting changes the same stock balance at the same moment. With a snapshot the count is a difference from it
    // (applied to the current balance); without one the counted quantity is the new on-hand.
    private async Task<Result<decimal>> AdjustCoreAsync(long companyId, long itemId, long warehouseId, DateOnly date, decimal countedQuantity, string adjustmentAccountCode, string? reference, string? reasonCode,
        decimal? snapshotQuantity, CancellationToken ct)
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
        decimal variance = countedQuantity - (snapshotQuantity ?? balance.QuantityOnHand);
        if (variance == 0m)
        {
            return 0m;
        }

        if (snapshotQuantity is not null && balance.QuantityOnHand + variance < 0m)
        {
            return Error.Rule("Inv.CountBelowMovements",
                $"Item '{item.Code}' was counted {Math.Abs(variance):0.###} short of the {snapshotQuantity:0.###} frozen, but only {balance.QuantityOnHand:0.###} is now on hand; recount it.");
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
    public async Task<Result> SetCatchWeightAsync(long companyId, long itemId, string? weightUnit, decimal? nominalWeight, decimal? tolerancePercent, CancellationToken ct = default)
    {
        Item? item = await _db.Items.FirstOrDefaultAsync(i => i.Id == itemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("Inv.ItemNotFound", $"Item {itemId} not found."));
        }

        string? unit = string.IsNullOrWhiteSpace(weightUnit) ? null : weightUnit.Trim().ToLowerInvariant();
        if (nominalWeight is <= 0m || tolerancePercent is < 0m or > 100m || (unit is null && (nominalWeight is not null || tolerancePercent is not null)) || unit?.Length > 10)
        {
            return Result.Failure(Error.Validation("Inv.CatchWeight", "A catch-weight item needs a weight unit; a nominal weight must be positive and a tolerance 0–100%."));
        }

        if (unit != item.CatchWeightUnit && await _db.StockBalances.AnyAsync(b => b.ItemId == itemId && b.QuantityOnHand != 0m, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Rule("Inv.CatchWeightStock", $"'{item.Code}' has stock on hand; its weight unit cannot change until it is cleared."));
        }

        item.CatchWeightUnit = unit;
        item.NominalWeight = unit is null ? null : nominalWeight;
        item.WeightTolerancePercent = unit is null ? null : tolerancePercent;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    // A catch-weight receipt: the weight is required and its average per piece must be within the tolerance of the nominal weight.
    private static Error? CatchWeightCheck(Item item, decimal quantity, decimal? weight)
    {
        if (weight is not > 0m)
        {
            return Error.Validation("Inv.WeightRequired", $"'{item.Code}' is catch-weight: weigh what is received.");
        }

        if (item.NominalWeight is decimal nominal && item.WeightTolerancePercent is decimal tolerance && quantity > 0m)
        {
            decimal average = weight.Value / quantity;
            if (Math.Abs(average - nominal) / nominal * 100m > tolerance)
            {
                return Error.Rule("Inv.WeightOutOfTolerance",
                    $"'{item.Code}' averages {average:0.###} {item.CatchWeightUnit} a piece, more than {tolerance:0.##}% from the nominal {nominal:0.###}.");
            }
        }

        return null;
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
        if (_db.Database.CurrentTransaction is not null)
        {
            // Twenty tills issuing the same item at once: the balance row is taken under lock inside the unit of work, so the second waits for the
            // first to commit and then reads the moved-on quantity, instead of failing on the concurrency token and retrying against the same race.
            await _db.Database.ExecuteSqlAsync($"SELECT id FROM inv.stock_balance WHERE company_id = {companyId} AND item_id = {itemId} AND warehouse_id = {warehouseId} FOR UPDATE", ct).ConfigureAwait(false);
        }

        StockBalance? balance = await _db.Set<StockBalance>()
            .FirstOrDefaultAsync(b => b.CompanyId == companyId && b.ItemId == itemId && b.WarehouseId == warehouseId, ct)
            .ConfigureAwait(false);

        if (balance is null)
        {
            balance = new StockBalance(companyId, itemId, warehouseId);
            _db.Set<StockBalance>().Add(balance);
        }
        else if (_db.Entry(balance).State == EntityState.Unchanged)
        {
            // A balance still tracked from an earlier movement in this context (a till's previous sale) keeps the quantity and concurrency token it had
            // then; another till may have moved it on since, so it is read again now that the row is ours.
            await _db.Entry(balance).ReloadAsync(ct).ConfigureAwait(false);
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
