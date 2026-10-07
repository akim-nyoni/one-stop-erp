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

/// <summary>Point of Sale service (brief §4.1): till sessions, sales with multi-currency tendering, and X/Z reads.</summary>
public sealed class PosService : IPosService
{
    private readonly Application.Abstractions.Licensing.ILicenceService? _licence;
    private readonly Application.Abstractions.Integration.IWebhookService? _webhooks;
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _postingEngine;
    private readonly IInventoryService _inventory;
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">The posting engine.</param>
    /// <param name="inventory">The inventory service.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="licence">Caps the POS terminals that may trade.</param>
    /// <param name="webhooks">Publishes completed sales.</param>
    public PosService(One4AllDbContext db, IPostingEngine postingEngine, IInventoryService inventory, IClock clock, Application.Abstractions.Licensing.ILicenceService? licence = null,
        Application.Abstractions.Integration.IWebhookService? webhooks = null)
    {
        _webhooks = webhooks;
        _licence = licence;
        _db = db;
        _postingEngine = postingEngine;
        _inventory = inventory;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<long>> OpenTillAsync(long companyId, long terminalId, long cashierUserId, decimal openingFloat, CancellationToken ct = default)
    {
        // The licence caps the terminals that may trade: the earliest registered active terminals, across companies (brief §11.5).
        if (_licence is { Enforced: true })
        {
            int place = await _db.PosTerminals.CountAsync(t => t.IsActive && t.Id <= terminalId, ct).ConfigureAwait(false);
            Result licensed = _licence.WithinLimit("pos_terminals", place, "POS");
            if (licensed.IsFailure)
            {
                return licensed.Error;
            }
        }

        TillSession session = new(companyId, terminalId, cashierUserId, _clock.UtcNow, openingFloat);
        _db.Set<TillSession>().Add(session);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return session.Id;
    }

    /// <inheritdoc />
    public Task<Result<CompleteSaleResult>> CompleteSaleAsync(CompleteSaleRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => CompleteSaleCoreAsync(request, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<CompleteSaleResult>> CompleteSaleCoreAsync(CompleteSaleRequest request, CancellationToken ct)
    {
        Company company = await _db.Companies.FirstAsync(c => c.Id == request.CompanyId, ct).ConfigureAwait(false);
        CurrencyCode baseCurrency = company.BaseCurrency;

        Sale sale = new(request.CompanyId, request.TerminalId, request.TillSessionId, request.CashierUserId, request.SaleNumber, request.SaleDate, baseCurrency)
        {
            AgeVerified = request.AgeVerified, PrescriptionReference = string.IsNullOrWhiteSpace(request.PrescriptionReference) ? null : request.PrescriptionReference.Trim(),
        };

        // Age-restricted, quantity-limited and prescription-only items (brief §4.1).
        List<long> soldIds = [.. request.Lines.Select(l => l.ItemId).Distinct()];
        Dictionary<long, Application.Pos.ItemSaleLimits> limits = await _db.Items.AsNoTracking()
            .Where(i => soldIds.Contains(i.Id) && (i.MinimumAge != null || i.MaxQuantityPerSale != null || i.RequiresPrescription))
            .Select(i => new Application.Pos.ItemSaleLimits(i.Id, i.Code, i.MinimumAge, i.MaxQuantityPerSale, i.RequiresPrescription))
            .ToDictionaryAsync(l => l.ItemId, ct).ConfigureAwait(false);
        Result allowed = Application.Pos.ItemSaleRules.Check(request.Lines.Select(l => (l.ItemId, l.Quantity)), limits, request.AgeVerified, request.PrescriptionReference);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        foreach (PosSaleLineRequest line in request.Lines)
        {
            if (line.PromotionDiscount < 0m || (line.PromotionDiscount > 0m && string.IsNullOrWhiteSpace(line.DiscountAccountCode)))
            {
                return Error.Validation("Pos.PromotionDiscount", "A promotion discount must be positive and name the account it posts to.");
            }

            sale.AddLine(new SaleLine(line.ItemId, line.Description, request.WarehouseId, line.Quantity, line.UnitPrice, line.LineNet, line.TaxAmount, line.RevenueAccountCode,
                line.PromotionDiscount, line.PromotionDiscount > 0m ? line.DiscountAccountCode : null, line.PromotionCode));
        }

        // Vouchers: each card must be active, unexpired, in the tender's currency and cover what is taken from it; no change is given from a voucher.
        Dictionary<string, (Core.Vouchers.GiftCard Card, decimal Amount)> vouchers = new(StringComparer.Ordinal);
        foreach (PosTenderRequest tender in request.Tenders.Where(t => t.TenderType == TenderType.Voucher))
        {
            if (tender.IsChange || string.IsNullOrWhiteSpace(tender.VoucherNumber) || tender.Amount <= 0m)
            {
                return Error.Validation("Pos.VoucherTender", "A voucher tender needs the gift card number and a positive amount, and gives no change.");
            }

            string number = tender.VoucherNumber.Trim().ToUpperInvariant();
            Core.Vouchers.GiftCard? card = vouchers.TryGetValue(number, out var held) ? held.Card
                : await _db.Set<Core.Vouchers.GiftCard>().FirstOrDefaultAsync(g => g.CompanyId == request.CompanyId && g.CardNumber == number, ct).ConfigureAwait(false);
            if (card is null)
            {
                return Error.NotFound("Voucher.NotFound", $"Gift card {number} not found.");
            }

            if (card.Status != Core.Vouchers.GiftCardStatus.Active || (card.ExpiryDate is DateOnly expiry && expiry < request.SaleDate))
            {
                return Error.Rule("Pos.VoucherNotUsable", $"Gift card {number} is {(card.Status != Core.Vouchers.GiftCardStatus.Active ? card.Status.ToString() : "expired")}.");
            }

            if (card.Currency != tender.Currency)
            {
                return Error.Rule("Pos.VoucherCurrency", $"Gift card {number} is in {card.Currency.Value}, not {tender.Currency.Value}.");
            }

            decimal taken = (held.Card is null ? 0m : held.Amount) + tender.Amount;
            if (taken > card.Balance)
            {
                return Error.Rule("Voucher.InsufficientBalance", $"Gift card {number} has {card.Balance:0.00}, which does not cover {taken:0.00}.");
            }

            vouchers[number] = (card, taken);
        }

        List<JournalLineRequest> journalLines = [];
        foreach (PosTenderRequest tender in request.Tenders)
        {
            Result<decimal> rateResult = await ResolveRateAsync(baseCurrency, tender.Currency, tender.ExchangeRate, request.SaleDate, ct).ConfigureAwait(false);
            if (rateResult.IsFailure)
            {
                return rateResult.Error;
            }

            // A staff purchase goes on the employee's staff account (a credit tender), recovered through payroll.
            if (tender.StaffEmployeeId is long staffId)
            {
                if (tender.TenderType != TenderType.Credit || tender.IsChange)
                {
                    return Error.Validation("Pos.StaffTender", "A staff purchase must be tendered on the staff account (a credit tender).");
                }

                if (!await _db.Employees.AnyAsync(e => e.Id == staffId && e.CompanyId == request.CompanyId && e.Status == One4All.Core.Payroll.EmployeeStatus.Active, ct).ConfigureAwait(false))
                {
                    return Error.Validation("Pos.StaffNotFound", "Staff purchases can only be charged to an active employee.");
                }
            }

            decimal rate = rateResult.Value;
            decimal baseAmount = decimal.Round(tender.Amount * rate, 4, MidpointRounding.ToEven);
            sale.AddTender(new SaleTender(tender.TenderType, tender.Currency, tender.Amount, rate, baseAmount, tender.AccountCode, tender.IsChange, tender.StaffEmployeeId,
                tender.TenderType == TenderType.Voucher ? tender.VoucherNumber!.Trim().ToUpperInvariant() : null));

            journalLines.Add(new JournalLineRequest { AccountCode = tender.AccountCode, Side = tender.IsChange ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = baseAmount });
        }

        // Revenue (net) per line, then output tax.
        // Revenue at the gross price; a promotion's discount to its own account so margin stays analysable (brief §4.1).
        foreach (SaleLine line in sale.Lines)
        {
            journalLines.Add(new JournalLineRequest { AccountCode = line.RevenueAccountCode, Side = JournalSide.Credit, TransactionAmount = line.LineNet + line.PromotionDiscount });
            if (line.PromotionDiscount > 0m)
            {
                journalLines.Add(new JournalLineRequest { AccountCode = line.DiscountAccountCode!, Side = JournalSide.Debit, TransactionAmount = line.PromotionDiscount, Narrative = line.PromotionCode });
            }
        }

        if (sale.TotalTax != 0m && request.TaxAccountCode is not null)
        {
            journalLines.Add(new JournalLineRequest { AccountCode = request.TaxAccountCode, Side = JournalSide.Credit, TransactionAmount = sale.TotalTax });
        }

        if (sale.Tenders.Where(t => t.TenderType == TenderType.Voucher).Sum(t => t.BaseAmount) > sale.TotalGross + 0.0001m)
        {
            return Error.Rule("Pos.VoucherOverpays", "Vouchers cannot pay more than the sale; no change is given from a voucher.");
        }

        decimal difference = sale.NetTenderedBase - sale.TotalGross;
        if (Math.Abs(difference) > 0.0001m)
        {
            // Cash rounding: a cash sale may be tendered up to half the smallest cash unit away from the exact total.
            bool rounds = company.CashRoundingIncrement is decimal increment && increment > 0m && !string.IsNullOrWhiteSpace(company.CashRoundingAccountCode)
                && Math.Abs(difference) <= (increment / 2m) + 0.0001m && sale.Tenders.Any(t => t.TenderType == TenderType.Cash);
            if (!rounds)
            {
                return Error.Rule("Pos.Unbalanced", $"Tenders {sale.NetTenderedBase:0.00} do not equal the sale total {sale.TotalGross:0.00}.");
            }

            journalLines.Add(new JournalLineRequest
            {
                AccountCode = company.CashRoundingAccountCode!, Side = difference > 0m ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = Math.Abs(difference), Narrative = "Cash rounding",
            });
            sale.RecordCashRounding(difference);
        }

        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = request.CompanyId,
            SourceModule = "POS",
            PostingDate = request.SaleDate,
            TransactionCurrency = baseCurrency,
            EnforceManualJournalRules = false,
            DocumentType = "PosSale",
            DocumentNumber = request.SaleNumber,
            Narrative = $"POS sale {request.SaleNumber}",
            Lines = journalLines,
        }, ct).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        sale.JournalEntryId = posting.Value.JournalId;
        _db.Set<Sale>().Add(sale);

        // The cards are charged only once the sale has posted.
        foreach ((string number, (Core.Vouchers.GiftCard card, decimal amount)) in vouchers)
        {
            Result redeemed = card.Redeem(amount);
            if (redeemed.IsFailure)
            {
                return redeemed.Error;
            }

            _db.Set<Core.Vouchers.GiftCardTransaction>().Add(new Core.Vouchers.GiftCardTransaction(card.CompanyId, card.Id, request.SaleDate, Core.Vouchers.GiftCardEvent.Redeemed, -amount, card.Balance,
                $"POS sale {request.SaleNumber}"));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Cost of sales: issue stock for stock-tracked lines.
        foreach (SaleLine line in sale.Lines)
        {
            Item? item = await _db.Set<Item>().FirstOrDefaultAsync(i => i.Id == line.ItemId, ct).ConfigureAwait(false);
            if (item is { IsStockTracked: true } && !string.IsNullOrEmpty(item.CostOfSalesAccountCode))
            {
                Result<StockMovementResult> issue = await _inventory.IssueAsync(new IssueStockRequest
                {
                    CompanyId = request.CompanyId,
                    ItemId = line.ItemId,
                    WarehouseId = request.WarehouseId,
                    Date = request.SaleDate,
                    Quantity = line.Quantity,
                    Reference = request.SaleNumber,
                    AllowNegative = true, // the sale is committed; let stock go negative and reconcile by stocktake
                }, ct).ConfigureAwait(false);

                if (issue.IsFailure)
                {
                    return issue.Error;
                }
            }
        }

        if (_webhooks is not null)
        {
            await _webhooks.PublishAsync(request.CompanyId, Application.Integration.WebhookEvents.SaleCompleted, new
            {
                request.SaleNumber, request.SaleDate, request.TerminalId, sale.TotalGross, Lines = request.Lines.Select(l => new { l.ItemId, l.Quantity, l.LineNet }),
            }, ct).ConfigureAwait(false);
        }

        return new CompleteSaleResult(sale.Id, posting.Value.JournalNumber, sale.TotalGross);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<CompleteSaleResult>>> SplitBillAsync(SplitBillRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => SplitBillCoreAsync(request, ct), ct);

    // Every part completes as its own sale inside one unit of work: one part refused undoes them all.
    private async Task<Result<IReadOnlyList<CompleteSaleResult>>> SplitBillCoreAsync(SplitBillRequest request, CancellationToken ct)
    {
        if (request.Tenders.Count != request.Parts.Count)
        {
            return Error.Validation("Pos.SplitTenders", "Each part of a split bill needs its own tenders.");
        }

        // The basket's own limits are checked on the whole basket, before it is divided.
        List<long> soldIds = [.. request.Basket.Lines.Select(l => l.ItemId).Distinct()];
        Dictionary<long, Application.Pos.ItemSaleLimits> limits = await _db.Items.AsNoTracking()
            .Where(i => soldIds.Contains(i.Id) && (i.MinimumAge != null || i.MaxQuantityPerSale != null || i.RequiresPrescription))
            .Select(i => new Application.Pos.ItemSaleLimits(i.Id, i.Code, i.MinimumAge, i.MaxQuantityPerSale, i.RequiresPrescription))
            .ToDictionaryAsync(l => l.ItemId, ct).ConfigureAwait(false);
        Result allowed = Application.Pos.ItemSaleRules.Check(request.Basket.Lines.Select(l => (l.ItemId, l.Quantity)), limits, request.Basket.AgeVerified, request.Basket.PrescriptionReference);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        Result<IReadOnlyList<IReadOnlyList<PosSaleLineRequest>>> plan = Application.Pos.SplitBill.Plan(request.Basket.Lines, request.Parts);
        if (plan.IsFailure)
        {
            return plan.Error;
        }

        List<CompleteSaleResult> results = [];
        for (int i = 0; i < plan.Value.Count; i++)
        {
            CompleteSaleRequest basket = request.Basket;
            Result<CompleteSaleResult> part = await CompleteSaleCoreAsync(new CompleteSaleRequest
            {
                CompanyId = basket.CompanyId, TerminalId = basket.TerminalId, TillSessionId = basket.TillSessionId, CashierUserId = basket.CashierUserId,
                SaleNumber = $"{basket.SaleNumber}-{i + 1}", SaleDate = basket.SaleDate, WarehouseId = basket.WarehouseId, TaxAccountCode = basket.TaxAccountCode,
                Lines = plan.Value[i], AgeVerified = basket.AgeVerified, PrescriptionReference = basket.PrescriptionReference, Tenders = request.Tenders[i],
            }, ct).ConfigureAwait(false);
            if (part.IsFailure)
            {
                return new Error(part.Error.Code, $"Part {i + 1}: {part.Error.Message}", part.Error.Type);
            }

            results.Add(part.Value);
        }

        return results;
    }

    /// <inheritdoc />
    public Task<Result<string>> VoidSaleAsync(long companyId, long saleId, DateOnly date, string reason, string? taxAccountCode = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => VoidSaleCoreAsync(companyId, saleId, date, reason, taxAccountCode, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<string>> VoidSaleCoreAsync(long companyId, long saleId, DateOnly date, string reason, string? taxAccountCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Pos.ReasonRequired", "Voiding a sale requires a reason.");
        }

        Sale? sale = await _db.Set<Sale>().Include(s => s.Lines).Include(s => s.Tenders)
            .FirstOrDefaultAsync(s => s.Id == saleId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (sale is null)
        {
            return Error.NotFound("Pos.SaleNotFound", $"Sale {saleId} not found.");
        }

        if (sale.Status != SaleStatus.Completed)
        {
            return Error.Rule("Pos.SaleNotCompleted", "Only a completed sale can be voided.");
        }

        if (sale.TotalTax != 0m && string.IsNullOrWhiteSpace(taxAccountCode))
        {
            return Error.Validation("Pos.TaxAccountRequired", "Voiding a sale with tax needs the output-tax account.");
        }

        // Inverse of the sale journal: refund tenders (Cr), unwind revenue and tax (Dr).
        List<JournalLineRequest> lines = [];
        foreach (SaleTender tender in sale.Tenders)
        {
            lines.Add(new JournalLineRequest { AccountCode = tender.AccountCode, Side = tender.IsChange ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = tender.BaseAmount });
        }

        foreach (SaleLine line in sale.Lines)
        {
            lines.Add(new JournalLineRequest { AccountCode = line.RevenueAccountCode, Side = JournalSide.Debit, TransactionAmount = line.LineNet + line.PromotionDiscount });
            if (line.PromotionDiscount > 0m)
            {
                lines.Add(new JournalLineRequest { AccountCode = line.DiscountAccountCode!, Side = JournalSide.Credit, TransactionAmount = line.PromotionDiscount, Narrative = line.PromotionCode });
            }
        }

        if (sale.TotalTax != 0m)
        {
            lines.Add(new JournalLineRequest { AccountCode = taxAccountCode!, Side = JournalSide.Debit, TransactionAmount = sale.TotalTax });
        }

        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "POS",
            PostingDate = date,
            TransactionCurrency = sale.BaseCurrency,
            EnforceManualJournalRules = false,
            DocumentType = "PosSaleVoid",
            DocumentNumber = sale.SaleNumber,
            Narrative = $"Void POS sale {sale.SaleNumber}: {reason}",
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        // Give back what the sale took from gift cards.
        foreach (IGrouping<string, SaleTender> paid in sale.Tenders.Where(t => t.TenderType == TenderType.Voucher && t.VoucherNumber is not null).GroupBy(t => t.VoucherNumber!))
        {
            Core.Vouchers.GiftCard? card = await _db.Set<Core.Vouchers.GiftCard>().FirstOrDefaultAsync(g => g.CompanyId == companyId && g.CardNumber == paid.Key, ct).ConfigureAwait(false);
            if (card is not null)
            {
                decimal amount = paid.Sum(t => t.Amount);
                card.Reinstate(amount);
                _db.Set<Core.Vouchers.GiftCardTransaction>().Add(new Core.Vouchers.GiftCardTransaction(companyId, card.Id, date, Core.Vouchers.GiftCardEvent.Reinstated, amount, card.Balance,
                    $"Void POS sale {sale.SaleNumber}"));
            }
        }

        // Reverse the cost-of-sales stock issues raised by the sale.
        List<StockMovement> issues = await _db.Set<StockMovement>()
            .Where(m => m.CompanyId == companyId && m.Reference == sale.SaleNumber && m.Type == StockMovementType.Issue && !m.IsReversed)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (StockMovement issue in issues)
        {
            Result<StockMovementResult> reversed = await _inventory.ReverseIssueAsync(companyId, issue.Id, date, $"VOID:{sale.SaleNumber}", ct).ConfigureAwait(false);
            if (reversed.IsFailure)
            {
                return reversed.Error;
            }
        }

        sale.Void();
        _db.PosEvents.Add(new PosEvent(companyId, sale.TerminalId, sale.CashierUserId, PosEventKind.SaleVoid, _clock.UtcNow, date, $"Sale {sale.SaleNumber}", sale.TotalNet + sale.TotalTax)
        {
            TillSessionId = sale.TillSessionId, SaleNumber = sale.SaleNumber, Reason = reason.Trim(), Reference = posting.Value.JournalNumber,
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return posting.Value.JournalNumber;
    }

    /// <inheritdoc />
    public Task<Result<TillRead>> XReadAsync(long tillSessionId, CancellationToken ct = default) => ReadAsync(tillSessionId, close: false, ct);

    /// <inheritdoc />
    public Task<Result<TillRead>> CloseTillAsync(long tillSessionId, CancellationToken ct = default) => ReadAsync(tillSessionId, close: true, ct);

    private async Task<Result<TillRead>> ReadAsync(long tillSessionId, bool close, CancellationToken ct)
    {
        TillSession? session = await _db.Set<TillSession>().FirstOrDefaultAsync(s => s.Id == tillSessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Error.NotFound("Pos.SessionNotFound", $"Till session {tillSessionId} not found.");
        }

        List<Sale> sales = await _db.Set<Sale>()
            .Include(s => s.Lines).Include(s => s.Tenders)
            .Where(s => s.TillSessionId == tillSessionId && s.Status == SaleStatus.Completed)
            .ToListAsync(ct).ConfigureAwait(false);

        decimal gross = sales.Sum(s => s.TotalGross);
        Dictionary<TenderType, decimal> tenderTotals = sales
            .SelectMany(s => s.Tenders)
            .Where(t => !t.IsChange)
            .GroupBy(t => t.TenderType)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.BaseAmount));

        int? zNumber = session.ZReadNumber;
        if (close && session.Status == TillSessionStatus.Open)
        {
            int? currentMax = await _db.Set<TillSession>()
                .Where(s => s.TerminalId == session.TerminalId && s.ZReadNumber != null)
                .MaxAsync(s => (int?)s.ZReadNumber, ct).ConfigureAwait(false);
            int nextZ = (currentMax ?? 0) + 1;
            session.Close(_clock.UtcNow, nextZ);
            zNumber = nextZ;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new TillRead(sales.Count, gross, tenderTotals, zNumber);
    }

    /// <inheritdoc />
    public Task<Result<CashUpResult>> CashUpAndCloseAsync(CashUpRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => CashUpAndCloseCoreAsync(request, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<CashUpResult>> CashUpAndCloseCoreAsync(CashUpRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CashAccountCode) || string.IsNullOrWhiteSpace(request.OverShortAccountCode))
        {
            return Error.Validation("Pos.CashUp.Accounts", "A cash-up needs both the cash and the cash-over/short account.");
        }

        TillSession? session = await _db.Set<TillSession>().FirstOrDefaultAsync(s => s.Id == request.TillSessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Error.NotFound("Pos.SessionNotFound", $"Till session {request.TillSessionId} not found.");
        }

        if (session.Status != TillSessionStatus.Open)
        {
            return Error.Rule("Pos.SessionClosed", "The till session is already closed.");
        }

        List<SaleTender> cashTenders = await _db.Set<Sale>()
            .Where(s => s.TillSessionId == session.Id && s.Status == SaleStatus.Completed)
            .SelectMany(s => s.Tenders)
            .Where(t => t.TenderType == TenderType.Cash)
            .ToListAsync(ct).ConfigureAwait(false);

        decimal cashReceived = cashTenders.Where(t => !t.IsChange).Sum(t => t.BaseAmount);
        decimal changeGiven = cashTenders.Where(t => t.IsChange).Sum(t => t.BaseAmount);
        // Pickups and paid-outs left the drawer; float top-ups went in.
        decimal movements = (await _db.TillCashMovements.Where(m => m.TillSessionId == session.Id).ToListAsync(ct).ConfigureAwait(false)).Sum(m => m.DrawerEffect);
        CashUpCalculation cashUp = new(session.OpeningFloat, cashReceived, changeGiven, request.CountedCash, movements);

        string? varianceJournal = null;
        if (!cashUp.Balances)
        {
            Company company = await _db.Companies.FirstAsync(c => c.Id == session.CompanyId, ct).ConfigureAwait(false);
            decimal amount = Math.Abs(cashUp.Variance);

            // Overage: cash is really higher than the books → Dr cash, Cr over/short (a gain).
            // Shortage: cash is really lower → Dr over/short (a loss), Cr cash.
            JournalSide cashSide = cashUp.IsOver ? JournalSide.Debit : JournalSide.Credit;
            JournalSide overShortSide = cashUp.IsOver ? JournalSide.Credit : JournalSide.Debit;

            Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
            {
                CompanyId = session.CompanyId,
                SourceModule = "POS",
                PostingDate = request.Date,
                TransactionCurrency = company.BaseCurrency,
                EnforceManualJournalRules = false,
                DocumentType = "PosCashUp",
                DocumentNumber = $"CASHUP-{session.Id}",
                Narrative = $"Cash-up variance for till session {session.Id} ({(cashUp.IsOver ? "over" : "short")} {amount:0.00})",
                Lines =
                [
                    new JournalLineRequest { AccountCode = request.CashAccountCode, Side = cashSide, TransactionAmount = amount },
                    new JournalLineRequest { AccountCode = request.OverShortAccountCode, Side = overShortSide, TransactionAmount = amount },
                ],
            }, ct).ConfigureAwait(false);

            if (posting.IsFailure)
            {
                return posting.Error;
            }

            varianceJournal = posting.Value.JournalNumber;
        }

        Result<TillRead> read = await ReadAsync(session.Id, close: true, ct).ConfigureAwait(false);
        if (read.IsFailure)
        {
            return read.Error;
        }

        return new CashUpResult(
            read.Value.ZReadNumber ?? 0,
            session.OpeningFloat,
            cashUp.ExpectedCash,
            cashUp.CountedCash,
            cashUp.Variance,
            varianceJournal,
            read.Value);
    }

    private async Task<Result<decimal>> ResolveRateAsync(CurrencyCode baseCurrency, CurrencyCode tenderCurrency, decimal? explicitRate, DateOnly date, CancellationToken ct)
    {
        if (tenderCurrency == baseCurrency)
        {
            return 1m;
        }

        if (explicitRate is { } r and > 0m)
        {
            return r;
        }

        decimal? rate = await Accounting.ExchangeRateResolver.FindAsync(_db, tenderCurrency, baseCurrency, ExchangeRateType.Daily, date, ct).ConfigureAwait(false);
        if (rate is null)
        {
            return Error.Rule("Pos.NoRate", $"No daily rate from {tenderCurrency} to {baseCurrency} on or before {date:yyyy-MM-dd}.");
        }

        return rate.Value;
    }
}
