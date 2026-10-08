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

/// <summary>Point of Sale service (brief §4.1): till sessions, sales with multi-currency tendering, and X/Z reads.</summary>
public sealed class PosService : IPosService
{
    private readonly Application.Abstractions.Licensing.ILicenceService? _licence;
    private readonly Application.Abstractions.Integration.IWebhookService? _webhooks;
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _postingEngine;
    private readonly IInventoryService _inventory;
    private readonly IClock _clock;
    private readonly IFiscalDeviceProvider? _fiscal;
    private readonly Application.Abstractions.Trading.IAllocationService? _allocations;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">The posting engine.</param>
    /// <param name="inventory">The inventory service.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="licence">Caps the POS terminals that may trade.</param>
    /// <param name="webhooks">Publishes completed sales.</param>
    /// <param name="fiscal">The fiscal device (brief §3.5); none or a disabled one means sales are not fiscalised.</param>
    /// <param name="allocations">Settles a voided on-account sale's invoice against its credit.</param>
    public PosService(One4AllDbContext db, IPostingEngine postingEngine, IInventoryService inventory, IClock clock, Application.Abstractions.Licensing.ILicenceService? licence = null,
        Application.Abstractions.Integration.IWebhookService? webhooks = null, IFiscalDeviceProvider? fiscal = null, Application.Abstractions.Trading.IAllocationService? allocations = null)
    {
        _allocations = allocations;
        _fiscal = fiscal;
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

        // The fiscal day opens with the first till of the day (the device keeps an open day open).
        if (_fiscal is { IsEnabled: true })
        {
            Result<int> day = await _fiscal.OpenDayAsync(companyId, ct).ConfigureAwait(false);
            _db.Set<PosEvent>().Add(new PosEvent(companyId, terminalId, cashierUserId, PosEventKind.FiscalSubmission, _clock.UtcNow, DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime),
                day.IsSuccess ? $"Fiscal day {day.Value} open" : $"Fiscal day not opened: {day.Error.Message}", 0m));
        }

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

        // On account: a credit tender (other than a staff purchase) charges the customer's account, which must be open, in the base currency
        // and within its credit limit; it posts to the receivables control account.
        decimal onAccount = request.Tenders.Where(t => t.TenderType == TenderType.Credit && t.StaffEmployeeId is null && !t.IsChange).Sum(t => t.Amount);
        Customer? accountCustomer = null;
        string? receivablesControl = null;
        if (onAccount > 0m)
        {
            if (request.CustomerId is not long customerId
                || await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false) is not Customer customer)
            {
                return Error.Validation("Pos.AccountCustomer", "An on-account sale needs the customer it is charged to.");
            }

            if (customer.OnHold)
            {
                return Error.Rule("Pos.CustomerOnHold", $"Customer {customer.Code} is on hold; nothing more can be charged to the account.");
            }

            if (customer.Currency != baseCurrency || request.Tenders.Any(t => t.TenderType == TenderType.Credit && t.StaffEmployeeId is null && t.Currency != baseCurrency))
            {
                return Error.Rule("Pos.AccountCurrency", $"Only accounts kept in {baseCurrency.Value} can be charged at the till.");
            }

            decimal owed = await _db.OpenItems.Where(i => i.CompanyId == request.CompanyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customer.Id)
                .SumAsync(i => i.Sign == LedgerSign.Debit ? i.OutstandingBase : -i.OutstandingBase, ct).ConfigureAwait(false);
            if (customer.CreditLimit > 0m && owed + onAccount > customer.CreditLimit)
            {
                return Error.Rule("Pos.CreditLimit", $"Customer {customer.Code} owes {owed:N2}; {onAccount:N2} more would pass the credit limit of {customer.CreditLimit:N2}.");
            }

            receivablesControl = await _db.Accounts.Where(a => a.CompanyId == request.CompanyId && a.ControlAccountType == ControlAccountType.AccountsReceivable)
                .Select(a => a.AccountCode).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (receivablesControl is null)
            {
                return Error.Rule("Trading.NoControlAccount", "No receivables control account is configured.");
            }

            accountCustomer = customer;
            sale.CustomerId = customer.Id;
        }

        List<JournalLineRequest> journalLines = [];
        foreach (PosTenderRequest tender in request.Tenders)
        {
            Result<decimal> rateResult = await ResolveRateAsync(baseCurrency, tender.Currency, tender.ExchangeRate, request.SaleDate, ct).ConfigureAwait(false);
            if (rateResult.IsFailure)
            {
                return rateResult.Error;
            }

            if (tender.IsChange && tender.Currency != baseCurrency && tender.Currency.Value != company.PosChangeCurrency)
            {
                return Error.Rule("Pos.ChangeCurrency", $"Change is given in {baseCurrency.Value}{(company.PosChangeCurrency is string cc ? " or " + cc : string.Empty)}, not {tender.Currency.Value}.");
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
            string tenderAccount = tender.TenderType == TenderType.Credit && tender.StaffEmployeeId is null && receivablesControl is not null ? receivablesControl : tender.AccountCode;
            sale.AddTender(new SaleTender(tender.TenderType, tender.Currency, tender.Amount, rate, baseAmount, tenderAccount, tender.IsChange, tender.StaffEmployeeId,
                tender.TenderType == TenderType.Voucher ? tender.VoucherNumber!.Trim().ToUpperInvariant() : null));

            // A foreign-currency tender goes to its account in that currency (so the drawer's dollars are revalued as dollars), at the till's rate.
            journalLines.Add(tender.Currency == baseCurrency
                ? new JournalLineRequest { AccountCode = tenderAccount, Side = tender.IsChange ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = baseAmount }
                : new JournalLineRequest
                {
                    AccountCode = tenderAccount, Side = tender.IsChange ? JournalSide.Credit : JournalSide.Debit, Currency = tender.Currency, TransactionAmount = tender.Amount,
                    ExchangeRate = rate, BaseAmount = baseAmount,
                });
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
            // Cash rounding: a cash sale may be tendered up to half the smallest cash unit away from the exact total — in base, or in the change
            // currency when change is given in it (half its smallest cash unit at the day's rate).
            decimal tolerance = company.CashRoundingIncrement is decimal increment && increment > 0m ? increment / 2m : 0m;
            if (company.PosChangeCurrency is string changeCode && changeCode != baseCurrency.Value)
            {
                CurrencyCode changeCurrency = CurrencyCode.Parse(changeCode);
                decimal? given = request.Tenders.Where(t => t.IsChange && t.Currency == changeCurrency).Select(t => t.ExchangeRate).FirstOrDefault();
                Result<decimal> changeRate = await ResolveRateAsync(baseCurrency, changeCurrency, given, request.SaleDate, ct).ConfigureAwait(false);
                decimal? smallest = await _db.Currencies.AsNoTracking().Include(c => c.Denominations).Where(c => c.Code == changeCurrency).Select(c => (decimal?)c.SmallestCashUnit)
                    .FirstOrDefaultAsync(ct).ConfigureAwait(false);
                if (changeRate.IsSuccess && smallest is decimal unit)
                {
                    tolerance = Math.Max(tolerance, unit * changeRate.Value / 2m);
                }
            }

            bool rounds = tolerance > 0m && !string.IsNullOrWhiteSpace(company.CashRoundingAccountCode)
                && Math.Abs(difference) <= tolerance + 0.0001m && sale.Tenders.Any(t => t.TenderType == TenderType.Cash);
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

        // The on-account part becomes an invoice on the customer's account, on the sale's journal.
        if (accountCustomer is not null)
        {
            _db.OpenItems.Add(new OpenItem(request.CompanyId, TradingLedger.AccountsReceivable, accountCustomer.Id, OpenItemType.Invoice, LedgerSign.Debit, request.SaleNumber, request.SaleDate,
                accountCustomer.DueDateFor(request.SaleDate), baseCurrency, 1m, onAccount, onAccount)
            {
                JournalEntryId = posting.Value.JournalId, Narrative = $"POS sale {request.SaleNumber} on account",
            });
        }

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
                    AllowNegative = true, FromTill = true, // the sale is committed; let stock go negative (recorded under the company's control) and reconcile by stocktake
                }, ct).ConfigureAwait(false);

                if (issue.IsFailure)
                {
                    return issue.Error;
                }
            }
        }

        // The receipt is signed by the fiscal device; an unreachable or refusing device queues it (the sale stands) for the retry task.
        if (_fiscal is { IsEnabled: true })
        {
            await FiscaliseAsync(sale, ct).ConfigureAwait(false);
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
            lines.Add(tender.Currency == sale.BaseCurrency
                ? new JournalLineRequest { AccountCode = tender.AccountCode, Side = tender.IsChange ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = tender.BaseAmount }
                : new JournalLineRequest
                {
                    AccountCode = tender.AccountCode, Side = tender.IsChange ? JournalSide.Debit : JournalSide.Credit, Currency = tender.Currency, TransactionAmount = tender.Amount,
                    ExchangeRate = tender.ExchangeRate, BaseAmount = tender.BaseAmount,
                });
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

        // An on-account sale: the voided amount is credited to the customer and set against the sale's invoice.
        decimal charged = sale.Tenders.Where(t => t.TenderType == TenderType.Credit && t.StaffEmployeeId is null && !t.IsChange).Sum(t => t.BaseAmount);
        if (sale.CustomerId is long accountId && charged > 0m)
        {
            OpenItem? invoice = await _db.OpenItems.FirstOrDefaultAsync(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == accountId
                && i.DocumentNumber == sale.SaleNumber && i.ItemType == OpenItemType.Invoice, ct).ConfigureAwait(false);
            OpenItem credit = new(companyId, TradingLedger.AccountsReceivable, accountId, OpenItemType.CreditNote, LedgerSign.Credit, $"VOID-{sale.SaleNumber}", date, date, sale.BaseCurrency, 1m,
                charged, charged)
            {
                JournalEntryId = posting.Value.JournalId, Narrative = $"Void of POS sale {sale.SaleNumber}: {reason}",
            };
            _db.OpenItems.Add(credit);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (invoice is not null && _allocations is not null && invoice.OutstandingBase > 0m)
            {
                Result<Application.Abstractions.Trading.AllocationResult> settled = await _allocations.AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoice.Id, credit.Id,
                    Math.Min(invoice.OutstandingTransaction, charged), date, ct).ConfigureAwait(false);
                if (settled.IsFailure)
                {
                    return settled.Error;
                }
            }
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
    public async Task<Result<TillRead>> CloseTillAsync(long tillSessionId, CancellationToken ct = default)
    {
        bool blind = await _db.Set<TillSession>().Where(s => s.Id == tillSessionId).Join(_db.PosTerminals, s => s.TerminalId, t => t.Id, (s, t) => t.BlindCashUp).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return blind ? Error.Rule("Pos.BlindCountMissing", "This till is cashed up blind: close it with a cash-up.") : await ReadAsync(tillSessionId, close: true, ct).ConfigureAwait(false);
    }

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

        Company company = await _db.Companies.FirstAsync(c => c.Id == session.CompanyId, ct).ConfigureAwait(false);
        bool blind = await _db.PosTerminals.AnyAsync(t => t.Id == session.TerminalId && t.BlindCashUp, ct).ConfigureAwait(false);
        List<TillCashCount> declared = await _db.Set<TillCashCount>().Where(c => c.TillSessionId == session.Id).ToListAsync(ct).ConfigureAwait(false);
        IReadOnlyList<CashCount> counts;
        if (declared.Count > 0)
        {
            if (request.Counts.Count > 0 || request.CountedCash != 0m)
            {
                return Error.Rule("Pos.CountDeclared", "The count was already declared; it cannot be changed at close.");
            }

            counts = [.. declared.Select(c => new CashCount(c.Currency, c.Amount))];
        }
        else if (blind)
        {
            return Error.Rule("Pos.BlindCountMissing", "This till is cashed up blind: declare the count before closing.");
        }
        else
        {
            Result<IReadOnlyList<CashCount>> given = CleanCounts(request.Counts.Count > 0 ? request.Counts : [new CashCount(company.BaseCurrency.Value, request.CountedCash)]);
            if (given.IsFailure)
            {
                return given.Error;
            }

            counts = given.Value;
            foreach (CashCount count in counts)
            {
                _db.Set<TillCashCount>().Add(new TillCashCount(session.CompanyId, session.Id, count.Currency, count.Amount, request.CountedByUserId, _clock.UtcNow, blind: false));
            }
        }

        Result<CashPosition> position = await PositionAsync(session, company.BaseCurrency.Value, counts, request.Date, ct).ConfigureAwait(false);
        if (position.IsFailure)
        {
            return position.Error;
        }

        IReadOnlyList<CurrencyCashUp> byCurrency = position.Value.ByCurrency;
        decimal varianceBase = byCurrency.Sum(c => c.VarianceBase);
        string? varianceJournal = null;
        if (byCurrency.Any(c => c.VarianceBase != 0m))
        {
            // Per currency: overage → Dr that currency's cash account; shortage → Cr it. The net goes to over/short (a gain or a loss).
            List<JournalLineRequest> lines = [.. byCurrency.Where(c => c.VarianceBase != 0m).Select(c => new JournalLineRequest
            {
                AccountCode = position.Value.CashAccounts.GetValueOrDefault(c.Currency) ?? request.CashAccountCode,
                Side = c.VarianceBase > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(c.VarianceBase),
                Narrative = $"{c.Currency} {(c.Variance > 0m ? "over" : "short")} {Math.Abs(c.Variance):0.00}",
            })];
            if (varianceBase != 0m)
            {
                lines.Add(new JournalLineRequest { AccountCode = request.OverShortAccountCode, Side = varianceBase > 0m ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = Math.Abs(varianceBase) });
            }

            Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
            {
                CompanyId = session.CompanyId,
                SourceModule = "POS",
                PostingDate = request.Date,
                TransactionCurrency = company.BaseCurrency,
                EnforceManualJournalRules = false,
                DocumentType = "PosCashUp",
                DocumentNumber = $"CASHUP-{session.Id}",
                Narrative = $"Cash-up variance for till session {session.Id} ({(varianceBase >= 0m ? "over" : "short")} {Math.Abs(varianceBase):0.00})",
                Lines = lines,
            }, ct).ConfigureAwait(false);

            if (posting.IsFailure)
            {
                return posting.Error;
            }

            varianceJournal = posting.Value.JournalNumber;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        CashUpCalculation cashUp = new(session.OpeningFloat, 0m, 0m, position.Value.ExpectedBase + varianceBase, position.Value.ExpectedBase - session.OpeningFloat);

        Result<TillRead> read = await ReadAsync(session.Id, close: true, ct).ConfigureAwait(false);
        if (read.IsFailure)
        {
            return read.Error;
        }

        // The last till closed closes the fiscal day (its Z report goes to the device).
        if (_fiscal is { IsEnabled: true }
            && !await _db.Set<TillSession>().AnyAsync(s => s.CompanyId == session.CompanyId && s.Id != session.Id && s.ClosedOnUtc == null, ct).ConfigureAwait(false))
        {
            Result closedDay = await _fiscal.CloseDayAsync(session.CompanyId, ct).ConfigureAwait(false);
            _db.Set<PosEvent>().Add(new PosEvent(session.CompanyId, session.TerminalId, session.CashierUserId, PosEventKind.FiscalSubmission, _clock.UtcNow, request.Date,
                closedDay.IsSuccess ? "Fiscal day closed" : $"Fiscal day not closed: {closedDay.Error.Message}", 0m));
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new CashUpResult(
            read.Value.ZReadNumber ?? 0,
            session.OpeningFloat,
            cashUp.ExpectedCash,
            cashUp.CountedCash,
            cashUp.Variance,
            varianceJournal,
            read.Value)
        {
            ByCurrency = byCurrency,
        };
    }

    /// <inheritdoc />
    public async Task<Result> DeclareCountAsync(long tillSessionId, IReadOnlyList<CashCount> counts, long userId, CancellationToken ct = default)
    {
        TillSession? session = await _db.Set<TillSession>().FirstOrDefaultAsync(s => s.Id == tillSessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Result.Failure(Error.NotFound("Pos.SessionNotFound", $"Till session {tillSessionId} not found."));
        }

        if (session.Status != TillSessionStatus.Open)
        {
            return Result.Failure(Error.Rule("Pos.SessionClosed", "The till session is already closed."));
        }

        if (await _db.Set<TillCashCount>().AnyAsync(c => c.TillSessionId == tillSessionId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Rule("Pos.CountDeclared", "The count for this till was already declared."));
        }

        Result<IReadOnlyList<CashCount>> clean = CleanCounts(counts);
        if (clean.IsFailure)
        {
            return Result.Failure(clean.Error);
        }

        bool blind = await _db.PosTerminals.AnyAsync(t => t.Id == session.TerminalId && t.BlindCashUp, ct).ConfigureAwait(false);
        foreach (CashCount count in clean.Value)
        {
            _db.Set<TillCashCount>().Add(new TillCashCount(session.CompanyId, session.Id, count.Currency, count.Amount, userId, _clock.UtcNow, blind));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<CurrencyCashUp>>> ExpectedCashAsync(long tillSessionId, CancellationToken ct = default)
    {
        TillSession? session = await _db.Set<TillSession>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == tillSessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Error.NotFound("Pos.SessionNotFound", $"Till session {tillSessionId} not found.");
        }

        List<CashCount> declared = await _db.Set<TillCashCount>().AsNoTracking().Where(c => c.TillSessionId == tillSessionId).Select(c => new CashCount(c.Currency, c.Amount))
            .ToListAsync(ct).ConfigureAwait(false);
        if (declared.Count == 0 && session.Status == TillSessionStatus.Open
            && await _db.PosTerminals.AnyAsync(t => t.Id == session.TerminalId && t.BlindCashUp, ct).ConfigureAwait(false))
        {
            return Error.Rule("Pos.BlindCountMissing", "This till is cashed up blind: the expected cash is shown once the count is declared.");
        }

        string baseCurrency = (await _db.Companies.AsNoTracking().FirstAsync(c => c.Id == session.CompanyId, ct).ConfigureAwait(false)).BaseCurrency.Value;
        Result<CashPosition> position = await PositionAsync(session, baseCurrency, declared, DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime), ct).ConfigureAwait(false);
        return position.IsFailure ? position.Error : Result<IReadOnlyList<CurrencyCashUp>>.Success(position.Value.ByCurrency);
    }

    private sealed record CashPosition(IReadOnlyList<CurrencyCashUp> ByCurrency, decimal ExpectedBase, IReadOnlyDictionary<string, string> CashAccounts);

    private static Result<IReadOnlyList<CashCount>> CleanCounts(IReadOnlyList<CashCount> counts)
    {
        List<CashCount> clean = [];
        foreach (CashCount count in counts)
        {
            string currency = (count.Currency ?? string.Empty).Trim().ToUpperInvariant();
            if (currency.Length != 3 || count.Amount < 0m)
            {
                return Error.Validation("Pos.CashCount", $"A cash count needs a 3-letter currency and an amount of zero or more ({count.Currency} {count.Amount}).");
            }

            if (clean.Any(c => c.Currency == currency))
            {
                return Error.Validation("Pos.CashCount", $"{currency} is counted twice.");
            }

            clean.Add(new CashCount(currency, count.Amount));
        }

        return clean.Count == 0 ? Error.Validation("Pos.CashCount", "Nothing was counted.") : clean;
    }

    // What the drawer should hold in each currency: the float, pickups / paid-outs / top-ups (base), and cash taken less change in the
    // currency it changed hands in. A foreign currency's variance is valued at the session's average rate for it (else the day's rate).
    private async Task<Result<CashPosition>> PositionAsync(TillSession session, string baseCurrency, IReadOnlyList<CashCount> counts, DateOnly date, CancellationToken ct)
    {
        List<SaleTender> cashTenders = await _db.Set<Sale>().AsNoTracking()
            .Where(s => s.TillSessionId == session.Id && s.Status == SaleStatus.Completed)
            .SelectMany(s => s.Tenders)
            .Where(t => t.TenderType == TenderType.Cash)
            .ToListAsync(ct).ConfigureAwait(false);
        decimal movements = (await _db.TillCashMovements.AsNoTracking().Where(m => m.TillSessionId == session.Id).ToListAsync(ct).ConfigureAwait(false)).Sum(m => m.DrawerEffect);

        Dictionary<string, List<SaleTender>> byCurrency = cashTenders.GroupBy(t => t.Currency.Value, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        SortedSet<string> currencies = new(StringComparer.Ordinal) { baseCurrency };
        currencies.UnionWith(byCurrency.Keys);
        currencies.UnionWith(counts.Select(c => c.Currency));

        List<CurrencyCashUp> lines = [];
        Dictionary<string, string> accounts = new(StringComparer.Ordinal);
        decimal expectedBase = session.OpeningFloat + movements;
        foreach (string currency in currencies)
        {
            List<SaleTender> tenders = byCurrency.GetValueOrDefault(currency) ?? [];
            decimal expected = tenders.Sum(t => t.IsChange ? -t.Amount : t.Amount) + (currency == baseCurrency ? session.OpeningFloat + movements : 0m);
            expectedBase += tenders.Sum(t => t.IsChange ? -t.BaseAmount : t.BaseAmount);
            decimal rate = 1m;
            if (currency != baseCurrency)
            {
                decimal amount = tenders.Sum(t => t.Amount);
                if (amount != 0m)
                {
                    rate = tenders.Sum(t => t.BaseAmount) / amount;
                }
                else
                {
                    Result<decimal> day = await ResolveRateAsync(CurrencyCode.Parse(baseCurrency), CurrencyCode.Parse(currency), null, date, ct).ConfigureAwait(false);
                    if (day.IsFailure)
                    {
                        return day.Error;
                    }

                    rate = day.Value;
                }
            }

            if (tenders.Count > 0)
            {
                accounts[currency] = tenders.GroupBy(t => t.AccountCode, StringComparer.Ordinal).OrderByDescending(g => g.Count()).First().Key;
            }

            decimal counted = counts.Where(c => c.Currency == currency).Sum(c => c.Amount);
            if (expected != 0m || counted != 0m || currency == baseCurrency)
            {
                lines.Add(new CurrencyCashUp(currency, expected, counted, rate));
            }
        }

        return new CashPosition(lines, expectedBase, accounts);
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<TillCurrency>> TenderCurrenciesAsync(long companyId, DateOnly date, CancellationToken ct = default)
    {
        CurrencyCode baseCurrency = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrency).SingleAsync(ct).ConfigureAwait(false);
        List<TillCurrency> currencies = [new TillCurrency(baseCurrency, 1m)];
        foreach (CurrencyCode code in await _db.Currencies.AsNoTracking().Select(c => c.Code).ToListAsync(ct).ConfigureAwait(false))
        {
            if (code != baseCurrency && await Accounting.ExchangeRateResolver.FindAsync(_db, code, baseCurrency, ExchangeRateType.Daily, date, ct).ConfigureAwait(false) is decimal rate && rate > 0m)
            {
                currencies.Add(new TillCurrency(code, rate));
            }
        }

        return currencies;
    }

    /// <inheritdoc />
    public async Task<Result> ConfigureChangeAsync(long companyId, string? changeCurrency, decimal? roundingIncrement, string? roundingAccountCode, CancellationToken ct = default)
    {
        Company company = await _db.Companies.FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        string? code = string.IsNullOrWhiteSpace(changeCurrency) ? null : changeCurrency.Trim().ToUpperInvariant();
        if (code is not null && code != company.BaseCurrency.Value
            && (!CurrencyCode.TryParse(code, out CurrencyCode parsed) || !await _db.Currencies.AnyAsync(c => c.Code == parsed, ct).ConfigureAwait(false)))
        {
            return Result.Failure(Error.NotFound("Pos.ChangeCurrency", $"No currency {code} is set up."));
        }

        if (roundingIncrement is < 0m || ((roundingIncrement > 0m || (code is not null && code != company.BaseCurrency.Value)) && string.IsNullOrWhiteSpace(roundingAccountCode)))
        {
            return Result.Failure(Error.Validation("Pos.ChangeRounding", "Rounding needs a non-negative increment and the account its differences go to."));
        }

        company.PosChangeCurrency = code == company.BaseCurrency.Value ? null : code;
        company.CashRoundingIncrement = roundingIncrement is > 0m ? roundingIncrement : null;
        company.CashRoundingAccountCode = string.IsNullOrWhiteSpace(roundingAccountCode) ? null : roundingAccountCode.Trim();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<PosChangeQuote?>> ChangeQuoteAsync(long companyId, decimal changeDueBase, DateOnly date, CancellationToken ct = default)
    {
        Company company = await _db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company.PosChangeCurrency is not string code || code == company.BaseCurrency.Value || changeDueBase <= 0m)
        {
            return Result<PosChangeQuote?>.Success(null);
        }

        CurrencyCode currency = CurrencyCode.Parse(code);
        Result<decimal> rate = await ResolveRateAsync(company.BaseCurrency, currency, null, date, ct).ConfigureAwait(false);
        if (rate.IsFailure)
        {
            return rate.Error;
        }

        Currency setup = await _db.Currencies.AsNoTracking().Include(c => c.Denominations).FirstAsync(c => c.Code == currency, ct).ConfigureAwait(false);
        return Result<PosChangeQuote?>.Success(PosChangeQuote.For(changeDueBase, currency, rate.Value, setup.SmallestCashUnit));
    }

    /// <inheritdoc />
    public async Task<(int Fiscalised, int StillQueued)> RetryFiscalAsync(long companyId, CancellationToken ct = default)
    {
        if (_fiscal is not { IsEnabled: true })
        {
            return (0, 0);
        }

        int signed = 0, waiting = 0;
        foreach (Sale sale in await _db.Set<Sale>().Include(s => s.Lines).Include(s => s.Tenders)
                     .Where(s => s.CompanyId == companyId && s.FiscalStatus == FiscalStatus.Queued).OrderBy(s => s.Id).Take(500).ToListAsync(ct).ConfigureAwait(false))
        {
            if (await FiscaliseAsync(sale, ct).ConfigureAwait(false))
            {
                signed++;
            }
            else
            {
                waiting++;
            }
        }

        return (signed, waiting);
    }

    // One attempt at signing a sale's receipt; the outcome is written to the sale and the till's event log.
    private async Task<bool> FiscaliseAsync(Sale sale, CancellationToken ct)
    {
        Result<FiscalReceiptResult> result;
        try
        {
            result = await _fiscal!.FiscaliseAsync(new FiscalReceiptRequest(sale.CompanyId, sale.SaleNumber, sale.TotalGross, sale.TotalTax, sale.BaseCurrency.Value, _clock.UtcNow), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or HttpRequestException or InvalidOperationException)
        {
            result = Error.Failure("Fiscal.Device", ex.Message);
        }

        bool signed = result.IsSuccess && !result.Value.Queued && !string.IsNullOrWhiteSpace(result.Value.FiscalCode);
        if (signed)
        {
            sale.RecordFiscal(result.Value.FiscalCode!, result.Value.SignatureOrQr, result.Value.FiscalDayNumber);
        }
        else
        {
            sale.QueueFiscal(result.IsFailure ? result.Error.Message : "The fiscal device queued the receipt.");
        }

        _db.Set<PosEvent>().Add(new PosEvent(sale.CompanyId, sale.TerminalId, sale.CashierUserId, PosEventKind.FiscalSubmission, _clock.UtcNow, sale.SaleDate,
            signed ? $"Sale {sale.SaleNumber} fiscalised {sale.FiscalCode}" : $"Sale {sale.SaleNumber} queued for the fiscal device: {sale.FiscalError}", sale.TotalGross)
        { SaleNumber = sale.SaleNumber });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return signed;
    }
}

/// <summary>Built-in task: send sales waiting for the fiscal device to it again.</summary>
public sealed class FiscalRetryTask : Application.Abstractions.Scheduling.IScheduledTask
{
    private readonly IPosService _pos;

    /// <summary>Creates the task.</summary>
    /// <param name="pos">The POS service.</param>
    public FiscalRetryTask(IPosService pos) => _pos = pos;

    /// <inheritdoc />
    public string Key => "pos.fiscal-retry";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        (int signed, int waiting) = await _pos.RetryFiscalAsync(companyId, ct).ConfigureAwait(false);
        string message = $"{signed} receipt(s) fiscalised, {waiting} still waiting for the fiscal device.";
        return waiting == 0 ? message : Error.Rule("Pos.FiscalPending", message);
    }
}
