using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Inventory;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>Applies landed charges to goods receipts (see <see cref="ILandedCostService"/>).</summary>
public sealed class LandedCostService : ILandedCostService
{
    private const string DocumentType = "LandedCost";

    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The posting engine.</param>
    public LandedCostService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public Task<Result<LandedCostResult>> ApplyAsync(LandedCostRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => ApplyCoreAsync(request, ct), ct);

    // One unit of work: the journal, the value movements and the stock balances.
    private async Task<Result<LandedCostResult>> ApplyCoreAsync(LandedCostRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0m)
        {
            return Error.Validation("Landed.Amount", "The landed charges must be positive.");
        }

        if (string.IsNullOrWhiteSpace(request.ClearingAccountCode) || string.IsNullOrWhiteSpace(request.Reference))
        {
            return Error.Validation("Landed.Details", "A clearing account and a reference are required.");
        }

        string reference = request.Reference.Trim();
        if (await _db.Journals.AnyAsync(j => j.CompanyId == request.CompanyId && j.DocumentType == DocumentType && j.DocumentNumber == reference, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Landed.Duplicate", $"Landed charges {reference} have already been applied.");
        }

        GoodsReceivedNote? grn = await _db.GoodsReceivedNotes.Include(g => g.Lines)
            .FirstOrDefaultAsync(g => g.Id == request.GoodsReceivedNoteId && g.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (grn is null || grn.Lines.Count == 0)
        {
            return Error.NotFound("Landed.GrnNotFound", $"Goods received note {request.GoodsReceivedNoteId} not found.");
        }

        string? stockCode = await _db.Accounts.Where(a => a.CompanyId == request.CompanyId && a.ControlAccountType == ControlAccountType.Stock)
            .Select(a => a.AccountCode).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (stockCode is null)
        {
            return Error.Rule("Landed.NoStockAccount", "No stock control account is configured.");
        }

        List<GrnLine> receiptLines = [.. grn.Lines.OrderBy(l => l.Id)];
        List<long> itemIds = [.. receiptLines.Select(l => l.ItemId).Distinct()];
        Dictionary<long, Item> items = await _db.Items.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct).ConfigureAwait(false);

        // Weight and volume bases need every item's measurements: a missing one would silently take no share.
        Item? unmeasured = receiptLines.Select(l => items[l.ItemId]).FirstOrDefault(i =>
            (request.Basis == LandedCostBasis.Weight && i.UnitWeightKg is null) || (request.Basis == LandedCostBasis.Volume && i.UnitVolumeM3 is null));
        if (unmeasured is not null)
        {
            string measure = request.Basis == LandedCostBasis.Weight ? "weight" : "volume";
            return Error.Rule("Landed.NoMeasurement", $"Item '{unmeasured.Code}' has no unit {measure} to share the charges by.");
        }

        decimal[] shares;
        try
        {
            shares = LandedCost.Apportion(
                [.. receiptLines.Select(l => new LandedCostLine(l.Quantity, l.Quantity * l.UnitPrice, l.Quantity * (items[l.ItemId].UnitWeightKg ?? 0m), l.Quantity * (items[l.ItemId].UnitVolumeM3 ?? 0m)))],
                request.Amount, request.Basis);
        }
        catch (ArgumentException ex)
        {
            return Error.Rule("Landed.NothingToApportion", ex.Message);
        }

        // Where each line went into stock: the receipt movement carries the GRN number.
        var receipts = await _db.StockMovements
            .Where(m => m.CompanyId == request.CompanyId && m.Reference == grn.GrnNumber && m.Type == StockMovementType.Receipt)
            .Select(m => new { m.ItemId, m.WarehouseId })
            .ToListAsync(ct).ConfigureAwait(false);

        List<LandedCostLineResult> results = [];
        List<(StockBalance Balance, long ItemId, long WarehouseId, decimal Amount)> capitalise = [];
        Dictionary<string, decimal> toCostOfSales = new(StringComparer.Ordinal);
        for (int i = 0; i < receiptLines.Count; i++)
        {
            GrnLine line = receiptLines[i];
            Item item = items[line.ItemId];
            long? warehouseId = receipts.FirstOrDefault(r => r.ItemId == line.ItemId)?.WarehouseId;
            StockBalance? balance = warehouseId is null ? null : await _db.StockBalances
                .FirstOrDefaultAsync(b => b.CompanyId == request.CompanyId && b.ItemId == line.ItemId && b.WarehouseId == warehouseId, ct).ConfigureAwait(false);

            // Only the share of what is still on hand can be capitalised; the rest belongs to cost of sales.
            decimal onHandShare = balance is null || line.Quantity <= 0m ? 0m : Math.Min(1m, Math.Max(0m, balance.QuantityOnHand) / line.Quantity);
            decimal capitalised = decimal.Round(shares[i] * onHandShare, 2, MidpointRounding.AwayFromZero);
            decimal expensed = shares[i] - capitalised;
            if (expensed != 0m)
            {
                if (string.IsNullOrEmpty(item.CostOfSalesAccountCode))
                {
                    return Error.Rule("Landed.NoCogsAccount", $"Item '{item.Code}' has no cost-of-sales account for the charges on stock already issued.");
                }

                toCostOfSales[item.CostOfSalesAccountCode] = toCostOfSales.GetValueOrDefault(item.CostOfSalesAccountCode) + expensed;
            }

            if (capitalised != 0m)
            {
                capitalise.Add((balance!, line.ItemId, warehouseId!.Value, capitalised));
            }

            results.Add(new LandedCostLineResult(item.Code, shares[i], capitalised, expensed));
        }

        List<JournalLineRequest> journal = [];
        decimal toStock = capitalise.Sum(c => c.Amount);
        if (toStock != 0m)
        {
            journal.Add(new JournalLineRequest { AccountCode = stockCode, Side = JournalSide.Debit, TransactionAmount = toStock });
        }

        journal.AddRange(toCostOfSales.Select(c => new JournalLineRequest { AccountCode = c.Key, Side = JournalSide.Debit, TransactionAmount = c.Value }));
        journal.Add(new JournalLineRequest { AccountCode = request.ClearingAccountCode.Trim(), Side = JournalSide.Credit, TransactionAmount = request.Amount });

        Company company = await _db.Companies.FirstAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = request.CompanyId,
            SourceModule = "INV",
            PostingDate = request.Date,
            TransactionCurrency = company.BaseCurrency,
            EnforceManualJournalRules = false,
            DocumentType = DocumentType,
            DocumentNumber = reference,
            Narrative = $"Landed charges on {grn.GrnNumber}",
            Lines = journal,
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        foreach ((StockBalance balance, long itemId, long warehouseId, decimal amount) in capitalise)
        {
            balance.AddValue(amount);
            _db.StockMovements.Add(new StockMovement(request.CompanyId, itemId, warehouseId, request.Date, StockMovementType.LandedCost, 0m, 0m, amount, reference)
            {
                JournalEntryId = posted.Value.JournalId,
            });
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new LandedCostResult(results, posted.Value.JournalNumber);
    }
}
