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
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.Pos;

/// <summary>Records till events and posts refunds against completed sales.</summary>
public sealed class PosEventService : IPosEventService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _postingEngine;
    private readonly Application.Abstractions.Security.IPermissionService? _permissions;
    private readonly IPosService? _pos;
    private readonly IInventoryService _inventory;
    private readonly IClock _clock;
    private readonly IFiscalDeviceProvider? _fiscal;
    private readonly Application.Abstractions.Trading.IAllocationService? _allocations;
    private readonly ILoyaltyService? _loyalty;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">Posts the refund journal.</param>
    /// <param name="inventory">Returns restocked goods.</param>
    /// <param name="clock">Stamps the events.</param>
    /// <param name="pos">Rings up the new sale of an exchange.</param>
    /// <param name="permissions">Checks the authoriser's refund authority.</param>
    /// <param name="fiscal">Signs refund receipts.</param>
    /// <param name="allocations">Sets an on-account refund's credit against the sale's invoice.</param>
    /// <param name="loyalty">Takes back the refunded share of the points the sale earned and gives back the share of those it redeemed.</param>
    public PosEventService(One4AllDbContext db, IPostingEngine postingEngine, IInventoryService inventory, IClock clock, IPosService? pos = null,
        Application.Abstractions.Security.IPermissionService? permissions = null, IFiscalDeviceProvider? fiscal = null, Application.Abstractions.Trading.IAllocationService? allocations = null,
        ILoyaltyService? loyalty = null)
    {
        _loyalty = loyalty;
        _allocations = allocations;
        _fiscal = fiscal;
        _permissions = permissions;
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
        => _db.AtomicAsync(() => RefundCoreAsync(request, exchange: false, ct), ct);

    // One unit of work: the refund journal, stock returns, the customer's credit, gift cards and refund events commit together or not at all.
    // An exchange's return pays straight into the new sale through the till's cash account, so it is not split over the original tenders
    // (ExchangeAsync checks any excess handed back in cash).
    private async Task<Result<PosRefundResult>> RefundCoreAsync(PosRefundRequest request, bool exchange, CancellationToken ct)
    {
        if (PosEventRules.Validate(PosEventKind.Refund, 0m, null, request.Reason, request.CashierUserId, request.AuthorisedByUserId, null) is { } invalid)
        {
            return invalid;
        }

        if (request.Lines.Count == 0 || request.Lines.Any(l => l.Quantity <= 0m))
        {
            return Error.Validation("Pos.RefundLines", "A refund needs at least one line with a positive quantity.");
        }

        Sale? sale = await _db.Set<Sale>().Include(s => s.Lines).Include(s => s.Tenders).FirstOrDefaultAsync(s => s.Id == request.SaleId && s.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (sale is null)
        {
            return Error.NotFound("Pos.SaleNotFound", $"Sale {request.SaleId} not found.");
        }

        if (sale.Status != SaleStatus.Completed)
        {
            return Error.Rule("Pos.SaleNotCompleted", "Only a completed sale can be refunded.");
        }

        // Every refund is authorised by a supervisor holding the refund authority (Pos.Refund at any level), not merely by another user;
        // past the refund window only the second level (AuthoriseLevel2) may approve it.
        Core.Security.EffectivePermissions rights = await AuthorityAsync(request.AuthorisedByUserId, ct).ConfigureAwait(false);
        if (!RefundLevels.Any(level => rights.Has("Pos.Refund", level)))
        {
            return Error.Forbidden("Pos.RefundAuthority", $"User {request.AuthorisedByUserId} may not authorise a refund.");
        }

        int? window = await _db.Companies.Where(c => c.Id == request.CompanyId).Select(c => c.RefundWindowDays).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (window is int days && request.Date > sale.SaleDate.AddDays(days) && !rights.Has("Pos.Refund", Core.Security.OperationFlags.AuthoriseLevel2))
        {
            return Error.Rule("Pos.RefundWindow", $"Sale {sale.SaleNumber} is more than {days} days old: the refund needs a supervisor with late-refund authority.");
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

        // The money goes back the way it came (or by the one method chosen); cash comes out of an open session of this till.
        Result<List<RefundLeg>> legs = PaymentLegs(sale, request, gross, exchange, rights);
        if (legs.IsFailure)
        {
            return legs.Error;
        }

        RefundLeg? cash = legs.Value.FirstOrDefault(l => l.Type == TenderType.Cash);
        if (cash is not null && await DrawerSessionAsync(request.CompanyId, request.TerminalId, request.TillSessionId, ct).ConfigureAwait(false) is Error noDrawer)
        {
            return noDrawer;
        }

        foreach (RefundLeg leg in legs.Value)
        {
            lines.Add(leg.Currency == sale.BaseCurrency || leg.Rate <= 0m
                ? new JournalLineRequest { AccountCode = leg.AccountCode, Side = JournalSide.Credit, TransactionAmount = leg.Base }
                : new JournalLineRequest
                {
                    AccountCode = leg.AccountCode, Side = JournalSide.Credit, Currency = leg.Currency, TransactionAmount = leg.InCurrency, ExchangeRate = leg.Rate, BaseAmount = leg.Base,
                });
        }

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
            foreach ((SaleLine line, decimal returned, _, _, _) in refunded)
            {
                // A kit's components go back at what they were issued at on the sale.
                foreach ((Item item, decimal quantity) in await Inventory.KitExplosion.StockLinesAsync(_db, request.CompanyId, line.ItemId, returned, ct).ConfigureAwait(false))
                {
                    StockMovement? issue = await _db.Set<StockMovement>().AsNoTracking()
                        .FirstOrDefaultAsync(m => m.CompanyId == request.CompanyId && m.Reference == sale.SaleNumber && m.ItemId == item.Id && m.Type == StockMovementType.Issue, ct).ConfigureAwait(false);
                    Result<StockMovementResult> back = await _inventory.ReceiveAsync(new ReceiveStockRequest
                    {
                        CompanyId = request.CompanyId, ItemId = item.Id, WarehouseId = line.WarehouseId, Date = request.Date, Quantity = quantity,
                        UnitCost = issue?.UnitCost ?? 0m, CreditAccountCode = item.CostOfSalesAccountCode!, Reference = $"REFUND:{sale.SaleNumber}",
                    }, ct).ConfigureAwait(false);
                    if (back.IsFailure)
                    {
                        return back.Error;
                    }
                }
            }
        }

        Result settled = await SettleLegsAsync(sale, request, legs.Value, posting.Value, ct).ConfigureAwait(false);
        if (settled.IsFailure)
        {
            return settled.Error;
        }

        // Points: the refunded share of what the sale earned is taken back (so buying, earning and refunding earns nothing), and the share of the
        // points tender paid back through it is given back to the member (its value returns to the liability on the refund journal).
        if (_loyalty is not null)
        {
            decimal pointsTendered = sale.Tenders.Where(t => t.TenderType == TenderType.Loyalty && !t.IsChange).Sum(t => t.BaseAmount);
            decimal earnedShare = sale.TotalGross > 0m ? Math.Min(1m, gross / sale.TotalGross) : 0m;
            decimal redeemedShare = pointsTendered > 0m ? Math.Min(1m, legs.Value.Where(l => l.Type == TenderType.Loyalty).Sum(l => l.Base) / pointsTendered) : 0m;
            Result<(decimal TakenBack, decimal GivenBack)> points = await _loyalty.ReverseSaleAsync(request.CompanyId, sale.SaleNumber, earnedShare, redeemedShare, request.Date,
                posting.Value.JournalNumber, ct).ConfigureAwait(false);
            if (points.IsFailure)
            {
                return points.Error;
            }
        }

        DateTimeOffset now = _clock.UtcNow;
        List<PosEvent> events = [.. refunded.Select(r => new PosEvent(request.CompanyId, request.TerminalId, request.CashierUserId, PosEventKind.Refund, now, request.Date, r.Line.Description, r.Net + r.Tax)
        {
            SaleNumber = sale.SaleNumber, ItemId = r.Line.ItemId, SaleLineId = r.Line.Id, Quantity = r.Quantity, OriginalAmount = r.Line.LineNet + r.Line.TaxAmount,
            Reason = request.Reason.Trim(), AuthorisedByUserId = request.AuthorisedByUserId, Reference = posting.Value.JournalNumber, Outcome = request.Restock ? "Restocked" : "Not restocked",
        })];
        if (await Accounting.FiscalSigning.SignAsync(_fiscal, new FiscalReceiptRequest(request.CompanyId, posting.Value.JournalNumber, gross, refunded.Sum(r => r.Tax), sale.BaseCurrency.Value, now,
                FiscalDocumentKind.Refund, sale.SaleNumber), ct).ConfigureAwait(false) is FiscalReceiptResult signed)
        {
            foreach (PosEvent e in events)
            {
                (e.FiscalCode, e.FiscalVerification, e.FiscalQueued) = (signed.FiscalCode, signed.SignatureOrQr, signed.Queued || signed.FiscalCode is null);
            }
        }

        _db.PosEvents.AddRange(events);
        if (cash is not null)
        {
            Result drawer = await PaidFromDrawerAsync(request.CompanyId, request.TillSessionId, cash.Base, cash.AccountCode, $"Refund on {sale.SaleNumber}", request.CashierUserId,
                request.AuthorisedByUserId, request.Date, posting.Value.JournalNumber, ct).ConfigureAwait(false);
            if (drawer.IsFailure)
            {
                return drawer.Error;
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new PosRefundResult(events[0].Id, posting.Value.JournalNumber, gross);
    }

    // The refund authority levels: any of them lets a supervisor authorise a refund inside the window.
    private static readonly Core.Security.OperationFlags[] RefundLevels =
        [Core.Security.OperationFlags.AuthoriseLevel1, Core.Security.OperationFlags.AuthoriseLevel2, Core.Security.OperationFlags.AuthoriseLevel3];

    // One way the refund is paid back: the method, the account it leaves from, its currency and rate, and its share of the refund in base.
    private sealed record RefundLeg(TenderType Type, string AccountCode, CurrencyCode Currency, decimal Rate, decimal Base, string? VoucherNumber, bool OnAccount)
    {
        // The share in the leg's own currency.
        public decimal InCurrency => Rate > 0m ? Math.Round(Base / Rate, 2, MidpointRounding.AwayFromZero) : Base;
    }

    private async Task<Core.Security.EffectivePermissions> AuthorityAsync(long? userId, CancellationToken ct)
        => _permissions is null || userId is not long id ? Core.Security.EffectivePermissions.None : await _permissions.ResolveAsync(id, ct).ConfigureAwait(false);

    // By default the refund goes back through the sale's own tenders, each getting its share in proportion to what it paid (net of change):
    // cash to the till's cash account, card and other tenders to their own accounts, on account to the customer, a gift card back onto the card.
    // Paying it all by one method the sale was not paid by (cash for a card sale) needs the third-level refund authority.
    private static Result<List<RefundLeg>> PaymentLegs(Sale sale, PosRefundRequest request, decimal gross, bool exchange, Core.Security.EffectivePermissions rights)
    {
        string? given = string.IsNullOrWhiteSpace(request.RefundAccountCode) ? null : request.RefundAccountCode.Trim();
        var paid = sale.Tenders
            .GroupBy(t => t.TenderType == TenderType.Cash
                ? new { Type = TenderType.Cash, Account = string.Empty, Currency = sale.BaseCurrency, Voucher = (string?)null, OnAccount = false }
                : new { Type = t.TenderType, Account = t.AccountCode, t.Currency, Voucher = t.VoucherNumber, OnAccount = t.TenderType == TenderType.Credit && t.StaffEmployeeId is null })
            .Select(g => new { g.Key, Rate = g.Key.Type == TenderType.Cash ? 1m : g.First().ExchangeRate, Base = g.Sum(t => t.IsChange ? -t.BaseAmount : t.BaseAmount) })
            .Where(p => p.Base > 0m)
            .OrderBy(p => p.Base)
            .ToList();

        if (exchange || request.RefundTender is not null)
        {
            TenderType method = exchange ? TenderType.Cash : request.RefundTender!.Value;
            if (method is not (TenderType.Cash or TenderType.Card or TenderType.MobileMoney or TenderType.BankTransfer or TenderType.Cheque))
            {
                return Error.Validation("Pos.RefundTender",
                    "A refund by another method is paid in cash, by card, mobile money, bank transfer or cheque; on account, gift cards and points take back their own tenders.");
            }

            if (given is null)
            {
                return Error.Validation("Pos.RefundAccount", $"A refund by {method} needs the account the money is paid from.");
            }

            bool same = paid.All(p => p.Key.Type == method && (method == TenderType.Cash || p.Key.Account == given));
            if (!exchange && !same && !rights.Has("Pos.Refund", Core.Security.OperationFlags.AuthoriseLevel3))
            {
                return Error.Forbidden("Pos.RefundMethod",
                    $"Sale {sale.SaleNumber} was not paid wholly by {method} from {given}: refunding it that way needs a supervisor with the authority to refund by another method.");
            }

            return new List<RefundLeg> { new(method, given, sale.BaseCurrency, 1m, gross, null, false) };
        }

        if (paid.Count == 0)
        {
            return Error.Rule("Pos.RefundTenders", $"Sale {sale.SaleNumber} has no tenders to refund through; choose the method to pay it back by.");
        }

        string? cashAccount = given ?? sale.Tenders.Where(t => t.TenderType == TenderType.Cash && t.Currency == sale.BaseCurrency).Select(t => t.AccountCode).FirstOrDefault();
        decimal total = paid.Sum(p => p.Base), left = gross;
        List<RefundLeg> legs = [];
        for (int i = 0; i < paid.Count; i++)
        {
            var tender = paid[i];
            decimal share = i == paid.Count - 1 ? left : Math.Round(gross * tender.Base / total, 2, MidpointRounding.AwayFromZero);
            left -= share;
            if (share <= 0m)
            {
                continue;
            }

            string? account = tender.Key.Type == TenderType.Cash ? cashAccount : tender.Key.Account;
            if (string.IsNullOrWhiteSpace(account))
            {
                return Error.Validation("Pos.RefundAccount", "The cash part of the refund needs the till's cash account it is paid from.");
            }

            legs.Add(new RefundLeg(tender.Key.Type, account, tender.Key.Currency, tender.Rate, share, tender.Key.Voucher, tender.Key.OnAccount));
        }

        return legs;
    }

    // A cash refund comes out of an open session of this till - the rule a sale is rung up under - so the drawer's expected cash falls with it.
    private async Task<Error?> DrawerSessionAsync(long companyId, long terminalId, long? tillSessionId, CancellationToken ct)
    {
        if (tillSessionId is not long sessionId)
        {
            return Error.Rule("Pos.SessionRequired", "A refund paid in cash comes out of an open session of this till.");
        }

        TillSession? session = await _db.Set<TillSession>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (session is null || session.TerminalId != terminalId)
        {
            return Error.Rule("Pos.SessionNotFound", $"Till session {sessionId} is not a session of this till.");
        }

        return session.Status != TillSessionStatus.Open
            ? Error.Rule("Pos.SessionClosed", $"Till session {sessionId} has been cashed up; the cash must be paid out of an open session.")
            : null;
    }

    // What was charged on account comes back as a credit note on the customer's account, set against the sale's invoice; a gift card gets its share back.
    private async Task<Result> SettleLegsAsync(Sale sale, PosRefundRequest request, IReadOnlyList<RefundLeg> legs, PostingReceipt posting, CancellationToken ct)
    {
        decimal onAccount = legs.Where(l => l.OnAccount).Sum(l => l.Base);
        if (onAccount > 0m)
        {
            if (sale.CustomerId is not long customerId)
            {
                return Result.Failure(Error.Rule("Pos.AccountCustomer", $"Sale {sale.SaleNumber} was charged on account but carries no customer to credit."));
            }

            OpenItem credit = new(request.CompanyId, TradingLedger.AccountsReceivable, customerId, OpenItemType.CreditNote, LedgerSign.Credit, posting.JournalNumber, request.Date, request.Date,
                sale.BaseCurrency, 1m, onAccount, onAccount)
            {
                JournalEntryId = posting.JournalId, Narrative = $"Refund on POS sale {sale.SaleNumber}: {request.Reason.Trim()}",
            };
            _db.OpenItems.Add(credit);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            OpenItem? invoice = await _db.OpenItems.FirstOrDefaultAsync(i => i.CompanyId == request.CompanyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customerId
                && i.DocumentNumber == sale.SaleNumber && i.ItemType == OpenItemType.Invoice, ct).ConfigureAwait(false);
            if (invoice is not null && _allocations is not null && invoice.OutstandingBase > 0m)
            {
                Result<Application.Abstractions.Trading.AllocationResult> matched = await _allocations.AllocateAsync(request.CompanyId, TradingLedger.AccountsReceivable, invoice.Id, credit.Id,
                    Math.Min(invoice.OutstandingTransaction, onAccount), request.Date, ct).ConfigureAwait(false);
                if (matched.IsFailure)
                {
                    return Result.Failure(matched.Error);
                }
            }
        }

        foreach (RefundLeg leg in legs.Where(l => l.Type == TenderType.Voucher && l.VoucherNumber is not null))
        {
            Core.Vouchers.GiftCard? card = await _db.Set<Core.Vouchers.GiftCard>().FirstOrDefaultAsync(g => g.CompanyId == request.CompanyId && g.CardNumber == leg.VoucherNumber, ct)
                .ConfigureAwait(false);
            if (card is null)
            {
                return Result.Failure(Error.NotFound("Voucher.NotFound", $"Gift card {leg.VoucherNumber} not found."));
            }

            decimal amount = leg.Currency == sale.BaseCurrency ? leg.Base : leg.InCurrency;
            card.Reinstate(amount);
            _db.Set<Core.Vouchers.GiftCardTransaction>().Add(new Core.Vouchers.GiftCardTransaction(request.CompanyId, card.Id, request.Date, Core.Vouchers.GiftCardEvent.Reinstated, amount,
                card.Balance, $"Refund on POS sale {sale.SaleNumber}"));
        }

        return Result.Success();
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
            }, exchange: true, ct).ConfigureAwait(false);
            if (returned.IsFailure)
            {
                return returned.Error;
            }

            decimal value = returned.Value.Gross;
            decimal newGross = request.NewLines.Sum(l => l.LineNet + l.TaxAmount);

            // Handing back in cash more than the new goods cost, on a sale not paid in cash, is a refund by another method.
            if (value > newGross
                && await _db.Set<Sale>().Where(s => s.Id == request.OriginalSaleId).SelectMany(s => s.Tenders).AnyAsync(t => t.TenderType != TenderType.Cash, ct).ConfigureAwait(false)
                && !(await AuthorityAsync(request.AuthorisedByUserId, ct).ConfigureAwait(false)).Has("Pos.Refund", Core.Security.OperationFlags.AuthoriseLevel3))
            {
                return Error.Forbidden("Pos.RefundMethod",
                    "The original sale was not paid in cash: handing the excess back in cash needs a supervisor with the authority to refund by another method.");
            }
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
                PriceOverrideAuthorisedByUserId = request.AuthorisedByUserId, // the supervisor of the exchange answers for a new price other than the list's
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
            DocumentNumber = document, Narrative = $"Refund without receipt: {item.Code} - {request.Reason.Trim()}", Lines = lines,
        }, ct).ConfigureAwait(false);
        if (posting.IsFailure)
        {
            return posting.Error;
        }

        if (request.RestockWarehouseId is long warehouse)
        {
            foreach ((Item stocked, decimal quantity) in await Inventory.KitExplosion.StockLinesAsync(_db, request.CompanyId, item.Id, request.Quantity, ct).ConfigureAwait(false))
            {
                decimal cost = await _db.StockBalances.Where(b => b.ItemId == stocked.Id && b.WarehouseId == warehouse).Select(b => b.AverageCost).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                Result<StockMovementResult> back = await _inventory.ReceiveAsync(new ReceiveStockRequest
                {
                    CompanyId = request.CompanyId, ItemId = stocked.Id, WarehouseId = warehouse, Date = request.Date, Quantity = quantity, UnitCost = cost,
                    CreditAccountCode = stocked.CostOfSalesAccountCode!, Reference = $"REFUND:{document}",
                }, ct).ConfigureAwait(false);
                if (back.IsFailure)
                {
                    return back.Error;
                }
            }
        }

        PosEvent refund = new(request.CompanyId, request.TerminalId, request.CashierUserId, PosEventKind.Refund, _clock.UtcNow, request.Date, item.Description, gross)
        {
            ItemId = item.Id, Quantity = request.Quantity, Reason = $"No receipt: {request.Reason.Trim()}", AuthorisedByUserId = request.AuthorisedByUserId, Reference = posting.Value.JournalNumber,
            Outcome = request.RestockWarehouseId is null ? "Not restocked" : "Restocked",
        };
        if (await Accounting.FiscalSigning.SignAsync(_fiscal, new FiscalReceiptRequest(request.CompanyId, posting.Value.JournalNumber, gross, request.TaxAmount, company.BaseCurrency.Value, _clock.UtcNow,
                FiscalDocumentKind.Refund), ct).ConfigureAwait(false) is FiscalReceiptResult signedRefund)
        {
            (refund.FiscalCode, refund.FiscalVerification, refund.FiscalQueued) = (signedRefund.FiscalCode, signedRefund.SignatureOrQr, signedRefund.Queued || signedRefund.FiscalCode is null);
        }

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
