using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.Pos;

/// <summary>Records till events and posts refunds against completed sales.</summary>
public sealed class PosEventService : IPosEventService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _postingEngine;
    private readonly IPosService? _pos;
    private readonly IInventoryService _inventory;
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">Posts the refund journal.</param>
    /// <param name="inventory">Returns restocked goods.</param>
    /// <param name="clock">Stamps the events.</param>
    public PosEventService(One4AllDbContext db, IPostingEngine postingEngine, IInventoryService inventory, IClock clock, IPosService? pos = null)
    {
        _pos = pos;
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

        List<(SaleLine Line, decimal Quantity, decimal Net, decimal Tax, decimal Discount)> refunded = [];
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
            refunded.Add((line, quantity, Math.Round(line.LineNet * share, 2, MidpointRounding.AwayFromZero), Math.Round(line.TaxAmount * share, 2, MidpointRounding.AwayFromZero),
                Math.Round(line.PromotionDiscount * share, 2, MidpointRounding.AwayFromZero)));
        }

        decimal tax = refunded.Sum(r => r.Tax), gross = refunded.Sum(r => r.Net) + tax;
        if (tax != 0m && string.IsNullOrWhiteSpace(request.TaxAccountCode))
        {
            return Error.Validation("Pos.TaxAccountRequired", "Refunding taxed lines needs the output-tax account.");
        }

        // Revenue comes back at the gross price and the promotion's share of the discount goes back to its account.
        List<JournalLineRequest> lines = [.. refunded.GroupBy(r => r.Line.RevenueAccountCode)
            .Select(g => new JournalLineRequest { AccountCode = g.Key, Side = JournalSide.Debit, TransactionAmount = g.Sum(r => r.Net + r.Discount) }),
            .. refunded.Where(r => r.Discount > 0m).GroupBy(r => r.Line.DiscountAccountCode!)
                .Select(g => new JournalLineRequest { AccountCode = g.Key, Side = JournalSide.Credit, TransactionAmount = g.Sum(r => r.Discount) })];
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
            foreach ((SaleLine line, decimal quantity, _, _, _) in refunded)
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
        Result drawer = await PaidFromDrawerAsync(request.CompanyId, request.TillSessionId, gross, request.RefundAccountCode, $"Refund on {sale.SaleNumber}", request.CashierUserId,
            request.AuthorisedByUserId, request.Date, posting.Value.JournalNumber, ct).ConfigureAwait(false);
        if (drawer.IsFailure)
        {
            return drawer.Error;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new PosRefundResult(events[0].Id, posting.Value.JournalNumber, gross);
    }

    /// <inheritdoc />
    public Task<Result<PosExchangeResult>> ExchangeAsync(PosExchangeRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            if (_pos is null)
            {
                return Error.Rule("Pos.ExchangeUnavailable", "Exchanges need the till service.");
            }

            if (request.NewLines.Count == 0 || string.IsNullOrWhiteSpace(request.CashAccountCode) || string.IsNullOrWhiteSpace(request.NewSaleNumber))
            {
                return Error.Validation("Pos.Exchange", "An exchange needs the goods going out, the new sale number and the till's cash account (the returns are checked like a refund).");
            }

            // The return, refunded through the drawer: the same value then tenders the new sale, so the drawer moves only by the difference.
            Result<PosRefundResult> returned = await RefundCoreAsync(new PosRefundRequest
            {
                CompanyId = request.CompanyId, SaleId = request.OriginalSaleId, TerminalId = request.TerminalId, CashierUserId = request.CashierUserId, AuthorisedByUserId = request.AuthorisedByUserId,
                Date = request.Date, RefundAccountCode = request.CashAccountCode, TaxAccountCode = request.TaxAccountCode, Reason = $"Exchange: {request.Reason}".Trim(), Lines = request.Returns,
                Restock = request.Restock, TillSessionId = request.TillSessionId,
            }, ct).ConfigureAwait(false);
            if (returned.IsFailure)
            {
                return returned.Error;
            }

            decimal value = returned.Value.Gross;
            decimal newGross = request.NewLines.Sum(l => l.LineNet + l.TaxAmount);
            CurrencyCode currency = await _db.Companies.Where(c => c.Id == request.CompanyId).Select(c => c.BaseCurrency).SingleAsync(ct).ConfigureAwait(false);
            List<PosTenderRequest> tenders = [new PosTenderRequest(TenderType.Cash, currency, value, request.CashAccountCode, IsChange: false)];
            if (value >= newGross)
            {
                if (value > newGross)
                {
                    tenders.Add(new PosTenderRequest(TenderType.Cash, currency, value - newGross, request.CashAccountCode, IsChange: true));
                }
            }
            else if (request.BalanceTenders.Count > 0)
            {
                tenders.AddRange(request.BalanceTenders);
            }
            else
            {
                tenders.Add(new PosTenderRequest(TenderType.Cash, currency, newGross - value, request.CashAccountCode, IsChange: false));
            }

            Result<CompleteSaleResult> sold = await _pos.CompleteSaleAsync(new CompleteSaleRequest
            {
                CompanyId = request.CompanyId, TerminalId = request.TerminalId, TillSessionId = request.TillSessionId, CashierUserId = request.CashierUserId, SaleNumber = request.NewSaleNumber.Trim(),
                SaleDate = request.Date, WarehouseId = request.WarehouseId, TaxAccountCode = request.TaxAccountCode, Lines = request.NewLines, Tenders = tenders,
            }, ct).ConfigureAwait(false);
            if (sold.IsFailure)
            {
                return sold.Error;
            }

            return Result<PosExchangeResult>.Success(new PosExchangeResult(returned.Value.JournalNumber, value, sold.Value.SaleId, sold.Value.TotalGross, sold.Value.TotalGross - value));
        }, ct);

    // Cash paid out of an open drawer for a refund counts against the cash the drawer should hold at cash-up.
    private async Task<Result> PaidFromDrawerAsync(long companyId, long? tillSessionId, decimal amount, string accountCode, string reason, long userId, long? authorisedBy, DateOnly date,
        string journalNumber, CancellationToken ct)
    {
        if (tillSessionId is not long sessionId || amount <= 0m)
        {
            return Result.Success();
        }

        if (!await _db.Set<TillSession>().AnyAsync(s => s.Id == sessionId && s.CompanyId == companyId && s.Status == TillSessionStatus.Open, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Rule("Pos.SessionClosed", $"Till session {sessionId} is not open."));
        }

        _db.TillCashMovements.Add(new TillCashMovement(companyId, sessionId, TillCashKind.Refund, amount, accountCode, reason, userId, authorisedBy, date, journalNumber));
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<PosRefundResult>> RefundWithoutReceiptAsync(UnreceiptedRefundRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => RefundWithoutReceiptCoreAsync(request, ct), ct);

    private async Task<Result<PosRefundResult>> RefundWithoutReceiptCoreAsync(UnreceiptedRefundRequest request, CancellationToken ct)
    {
        if (request.AuthorisedByUserId is null || request.AuthorisedByUserId == request.CashierUserId)
        {
            return Error.Rule("Pos.AuthorisationRequired", "A refund without a receipt needs a supervisor other than the cashier.");
        }

        if (request.Quantity <= 0m || request.UnitPrice <= 0m || request.TaxAmount < 0m || string.IsNullOrWhiteSpace(request.Reason) || string.IsNullOrWhiteSpace(request.RefundAccountCode))
        {
            return Error.Validation("Pos.UnreceiptedInvalid", "The refund needs a quantity, a price, a reason and the account the money is paid from.");
        }

        if (request.TaxAmount > 0m && string.IsNullOrWhiteSpace(request.TaxAccountCode))
        {
            return Error.Validation("Pos.TaxAccountRequired", "Refunding tax needs the output-tax account.");
        }

        Company company = await _db.Companies.AsNoTracking().FirstAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);
        decimal net = decimal.Round(request.Quantity * request.UnitPrice, 2, MidpointRounding.AwayFromZero);
        decimal gross = net + request.TaxAmount;
        if (company.UnreceiptedRefundLimit is not decimal limit)
        {
            return Error.Rule("Pos.UnreceiptedNotAllowed", "This company does not refund without a receipt.");
        }

        if (gross > limit)
        {
            return Error.Rule("Pos.UnreceiptedLimit", $"Refunds without a receipt are limited to {limit:N2}.");
        }

        Item? item = await _db.Set<Item>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.ItemId && i.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (item is null || string.IsNullOrWhiteSpace(item.RevenueAccountCode))
        {
            return Error.NotFound("Pos.ItemNotFound", "The item returned must exist and have a revenue account.");
        }

        if (!await _db.PosTerminals.AnyAsync(t => t.Id == request.TerminalId && t.CompanyId == request.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Pos.TerminalNotFound", $"Till {request.TerminalId} not found.");
        }

        List<JournalLineRequest> lines = [new JournalLineRequest { AccountCode = item.RevenueAccountCode, Side = JournalSide.Debit, TransactionAmount = net }];
        if (request.TaxAmount > 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = request.TaxAccountCode!, Side = JournalSide.Debit, TransactionAmount = request.TaxAmount });
        }

        lines.Add(new JournalLineRequest { AccountCode = request.RefundAccountCode, Side = JournalSide.Credit, TransactionAmount = gross });
        int sequence = await _db.PosEvents.CountAsync(e => e.CompanyId == request.CompanyId && e.Kind == PosEventKind.Refund && e.SaleNumber == null, ct).ConfigureAwait(false) + 1;
        string document = $"NR-{request.Date:yyyyMMdd}-{sequence}";
        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = request.CompanyId, SourceModule = "POS", PostingDate = request.Date, TransactionCurrency = company.BaseCurrency, EnforceManualJournalRules = false, DocumentType = "PosRefund",
            DocumentNumber = document, Narrative = $"Refund without receipt: {item.Code} — {request.Reason.Trim()}", Lines = lines,
        }, ct).ConfigureAwait(false);
        if (posting.IsFailure)
        {
            return posting.Error;
        }

        if (request.RestockWarehouseId is long warehouse && item.IsStockTracked && !string.IsNullOrEmpty(item.CostOfSalesAccountCode))
        {
            decimal cost = await _db.StockBalances.Where(b => b.ItemId == item.Id && b.WarehouseId == warehouse).Select(b => b.AverageCost).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            Result<StockMovementResult> back = await _inventory.ReceiveAsync(new ReceiveStockRequest
            {
                CompanyId = request.CompanyId, ItemId = item.Id, WarehouseId = warehouse, Date = request.Date, Quantity = request.Quantity, UnitCost = cost, CreditAccountCode = item.CostOfSalesAccountCode,
                Reference = $"REFUND:{document}",
            }, ct).ConfigureAwait(false);
            if (back.IsFailure)
            {
                return back.Error;
            }
        }

        PosEvent refund = new(request.CompanyId, request.TerminalId, request.CashierUserId, PosEventKind.Refund, _clock.UtcNow, request.Date, item.Description, gross)
        {
            ItemId = item.Id, Quantity = request.Quantity, Reason = $"No receipt: {request.Reason.Trim()}", AuthorisedByUserId = request.AuthorisedByUserId, Reference = posting.Value.JournalNumber,
            Outcome = request.RestockWarehouseId is null ? "Not restocked" : "Restocked",
        };
        _db.PosEvents.Add(refund);
        Result drawer = await PaidFromDrawerAsync(request.CompanyId, request.TillSessionId, gross, request.RefundAccountCode, $"Refund without receipt: {item.Description}", request.CashierUserId,
            request.AuthorisedByUserId, request.Date, posting.Value.JournalNumber, ct).ConfigureAwait(false);
        if (drawer.IsFailure)
        {
            return drawer.Error;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new PosRefundResult(refund.Id, posting.Value.JournalNumber, gross);
    }
}
