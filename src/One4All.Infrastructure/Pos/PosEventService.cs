using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.Pos;

/// <summary>Records till events and posts refunds against completed sales.</summary>
public sealed class PosEventService : IPosEventService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _postingEngine;
    private readonly IInventoryService _inventory;
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">Posts the refund journal.</param>
    /// <param name="inventory">Returns restocked goods.</param>
    /// <param name="clock">Stamps the events.</param>
    public PosEventService(One4AllDbContext db, IPostingEngine postingEngine, IInventoryService inventory, IClock clock)
    {
        _db = db;
        _postingEngine = postingEngine;
        _inventory = inventory;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<long>> RecordAsync(PosEventRequest request, CancellationToken ct = default)
    {
        if (request.Kind == PosEventKind.Refund)
        {
            return Error.Rule("Pos.RefundThroughService", "Refunds are posted with RefundAsync so the money and stock move with the record.");
        }

        if (PosEventRules.Validate(request.Kind, request.Amount, request.OriginalAmount, request.Reason, request.CashierUserId, request.AuthorisedByUserId, request.CustomerReference) is { } invalid)
        {
            return invalid;
        }

        if (!await _db.Set<PosTerminal>().AnyAsync(t => t.Id == request.TerminalId && t.CompanyId == request.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Pos.TerminalNotFound", $"Till {request.TerminalId} not found.");
        }

        PosEvent recorded = new(request.CompanyId, request.TerminalId, request.CashierUserId, request.Kind, _clock.UtcNow, request.Date, request.Description.Trim(), request.Amount)
        {
            TillSessionId = request.TillSessionId,
            SaleNumber = request.SaleNumber,
            ItemId = request.ItemId,
            OriginalAmount = request.OriginalAmount,
            Reason = request.Reason?.Trim(),
            AuthorisedByUserId = request.AuthorisedByUserId,
            CustomerReference = request.CustomerReference?.Trim(),
            Reference = request.Reference,
            Outcome = request.Outcome,
        };
        _db.PosEvents.Add(recorded);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return recorded.Id;
    }

    /// <inheritdoc />
    public Task<Result<PosRefundResult>> RefundAsync(PosRefundRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => RefundCoreAsync(request, ct), ct);

    // One unit of work: the refund journal, stock returns and refund events commit together or not at all.
    private async Task<Result<PosRefundResult>> RefundCoreAsync(PosRefundRequest request, CancellationToken ct)
    {
        if (PosEventRules.Validate(PosEventKind.Refund, 0m, null, request.Reason, request.CashierUserId, request.AuthorisedByUserId, null) is { } invalid)
        {
            return invalid;
        }

        if (request.Lines.Count == 0 || request.Lines.Any(l => l.Quantity <= 0m))
        {
            return Error.Validation("Pos.RefundLines", "A refund needs at least one line with a positive quantity.");
        }

        if (string.IsNullOrWhiteSpace(request.RefundAccountCode))
        {
            return Error.Validation("Pos.RefundAccount", "A refund needs the account the money is paid from.");
        }

        Sale? sale = await _db.Set<Sale>().Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == request.SaleId && s.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (sale is null)
        {
            return Error.NotFound("Pos.SaleNotFound", $"Sale {request.SaleId} not found.");
        }

        if (sale.Status != SaleStatus.Completed)
        {
            return Error.Rule("Pos.SaleNotCompleted", "Only a completed sale can be refunded.");
        }

        Dictionary<long, decimal> alreadyRefunded = await _db.PosEvents.AsNoTracking()
            .Where(e => e.CompanyId == request.CompanyId && e.Kind == PosEventKind.Refund && e.SaleNumber == sale.SaleNumber && e.SaleLineId != null)
            .GroupBy(e => e.SaleLineId!.Value).Select(g => new { g.Key, Quantity = g.Sum(e => e.Quantity ?? 0m) })
            .ToDictionaryAsync(g => g.Key, g => g.Quantity, ct).ConfigureAwait(false);

        List<(SaleLine Line, decimal Quantity, decimal Net, decimal Tax)> refunded = [];
        foreach (IGrouping<long, PosRefundLine> group in request.Lines.GroupBy(l => l.SaleLineId))
        {
            SaleLine? line = sale.Lines.FirstOrDefault(l => l.Id == group.Key);
            if (line is null)
            {
                return Error.NotFound("Pos.SaleLineNotFound", $"Line {group.Key} is not on sale {sale.SaleNumber}.");
            }

            decimal quantity = group.Sum(l => l.Quantity);
            decimal left = line.Quantity - alreadyRefunded.GetValueOrDefault(line.Id);
            if (quantity > left)
            {
                return Error.Rule("Pos.RefundExceedsSale", $"Only {left:0.###} of '{line.Description}' remain to refund on sale {sale.SaleNumber}.");
            }

            decimal share = quantity / line.Quantity;
            refunded.Add((line, quantity, Math.Round(line.LineNet * share, 2, MidpointRounding.AwayFromZero), Math.Round(line.TaxAmount * share, 2, MidpointRounding.AwayFromZero)));
        }

        decimal tax = refunded.Sum(r => r.Tax), gross = refunded.Sum(r => r.Net) + tax;
        if (tax != 0m && string.IsNullOrWhiteSpace(request.TaxAccountCode))
        {
            return Error.Validation("Pos.TaxAccountRequired", "Refunding taxed lines needs the output-tax account.");
        }

        List<JournalLineRequest> lines = [.. refunded.GroupBy(r => r.Line.RevenueAccountCode)
            .Select(g => new JournalLineRequest { AccountCode = g.Key, Side = JournalSide.Debit, TransactionAmount = g.Sum(r => r.Net) })];
        if (tax != 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = request.TaxAccountCode!, Side = JournalSide.Debit, TransactionAmount = tax });
        }

        lines.Add(new JournalLineRequest { AccountCode = request.RefundAccountCode, Side = JournalSide.Credit, TransactionAmount = gross });
        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = request.CompanyId,
            SourceModule = "POS",
            PostingDate = request.Date,
            TransactionCurrency = sale.BaseCurrency,
            EnforceManualJournalRules = false,
            DocumentType = "PosRefund",
            DocumentNumber = sale.SaleNumber,
            Narrative = $"Refund on POS sale {sale.SaleNumber}: {request.Reason.Trim()}",
            Lines = lines,
        }, ct).ConfigureAwait(false);
        if (posting.IsFailure)
        {
            return posting.Error;
        }

        if (request.Restock)
        {
            foreach ((SaleLine line, decimal quantity, _, _) in refunded)
            {
                Item? item = await _db.Set<Item>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == line.ItemId, ct).ConfigureAwait(false);
                if (item is not { IsStockTracked: true } || string.IsNullOrEmpty(item.CostOfSalesAccountCode))
                {
                    continue;
                }

                StockMovement? issue = await _db.Set<StockMovement>().AsNoTracking()
                    .FirstOrDefaultAsync(m => m.CompanyId == request.CompanyId && m.Reference == sale.SaleNumber && m.ItemId == line.ItemId && m.Type == StockMovementType.Issue, ct).ConfigureAwait(false);
                Result<StockMovementResult> back = await _inventory.ReceiveAsync(new ReceiveStockRequest
                {
                    CompanyId = request.CompanyId, ItemId = line.ItemId, WarehouseId = line.WarehouseId, Date = request.Date, Quantity = quantity,
                    UnitCost = issue?.UnitCost ?? 0m, CreditAccountCode = item.CostOfSalesAccountCode, Reference = $"REFUND:{sale.SaleNumber}",
                }, ct).ConfigureAwait(false);
                if (back.IsFailure)
                {
                    return back.Error;
                }
            }
        }

        DateTimeOffset now = _clock.UtcNow;
        List<PosEvent> events = [.. refunded.Select(r => new PosEvent(request.CompanyId, request.TerminalId, request.CashierUserId, PosEventKind.Refund, now, request.Date, r.Line.Description, r.Net + r.Tax)
        {
            SaleNumber = sale.SaleNumber, ItemId = r.Line.ItemId, SaleLineId = r.Line.Id, Quantity = r.Quantity, OriginalAmount = r.Line.LineNet + r.Line.TaxAmount,
            Reason = request.Reason.Trim(), AuthorisedByUserId = request.AuthorisedByUserId, Reference = posting.Value.JournalNumber, Outcome = request.Restock ? "Restocked" : "Not restocked",
        })];
        _db.PosEvents.AddRange(events);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new PosRefundResult(events[0].Id, posting.Value.JournalNumber, gross);
    }
}
