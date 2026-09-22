using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>
/// Implements the Sales Ledger, Purchase Ledger and allocation services (brief §4.5/§4.6). Every document
/// posts through the central posting engine (never a module-local journal) and creates an open item;
/// allocation matches items and realises FX from their differing base rates.
/// </summary>
public sealed class TradingService : ISalesLedgerService, IPurchaseLedgerService, IAllocationService
{
    private const int Scale = 4;
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _postingEngine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">The posting engine.</param>
    public TradingService(One4AllDbContext db, IPostingEngine postingEngine)
    {
        _db = db;
        _postingEngine = postingEngine;
    }

    /// <inheritdoc />
    public async Task<Result<TradingPostingResult>> PostInvoiceAsync(SalesInvoiceRequest request, CancellationToken ct = default)
    {
        Customer? customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return Error.NotFound("Ar.CustomerNotFound", $"Customer {request.CustomerId} not found.");
        }

        if (customer.OnHold)
        {
            return Error.Rule("Ar.CustomerOnHold", $"Customer {customer.Code} is on hold; no new invoices can be posted.");
        }

        Result<string> controlResult = await ControlCodeAsync(request.CompanyId, ControlAccountType.AccountsReceivable, ct).ConfigureAwait(false);
        if (controlResult.IsFailure)
        {
            return controlResult.Error;
        }

        Result<decimal> rateResult = await ResolveRateAsync(request.CompanyId, request.Currency, request.ExchangeRate, request.DocumentDate, ct).ConfigureAwait(false);
        if (rateResult.IsFailure)
        {
            return rateResult.Error;
        }

        decimal rate = rateResult.Value;
        decimal gross = request.NetAmount + request.TaxAmount;
        decimal baseNet = Round(request.NetAmount * rate);
        decimal baseTax = Round(request.TaxAmount * rate);
        decimal baseGross = baseNet + baseTax;

        // Credit-limit control: refuse when this invoice would take the customer's outstanding balance over the limit.
        if (customer.CreditLimit > 0m)
        {
            IQueryable<OpenItem> ar = _db.OpenItems.Where(i => i.CompanyId == request.CompanyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customer.Id);
            decimal debits = await ar.Where(i => i.Sign == LedgerSign.Debit).SumAsync(i => (decimal?)i.OutstandingBase, ct).ConfigureAwait(false) ?? 0m;
            decimal credits = await ar.Where(i => i.Sign == LedgerSign.Credit).SumAsync(i => (decimal?)i.OutstandingBase, ct).ConfigureAwait(false) ?? 0m;
            decimal outstanding = debits - credits;
            if (outstanding + baseGross > customer.CreditLimit)
            {
                return Error.Rule("Ar.CreditLimitExceeded", $"Customer {customer.Code}: this invoice would take the balance to {outstanding + baseGross:0.00}, over the credit limit of {customer.CreditLimit:0.00}.");
            }
        }

        List<JournalLineRequest> lines =
        [
            Line(controlResult.Value, JournalSide.Debit, gross, rate, baseGross),
            Line(request.SalesAccountCode, JournalSide.Credit, request.NetAmount, rate, baseNet),
        ];
        if (request.TaxAmount != 0m && request.TaxAccountCode is not null)
        {
            lines.Add(Line(request.TaxAccountCode, JournalSide.Credit, request.TaxAmount, rate, baseTax));
        }

        return await PostAndOpenAsync(request.CompanyId, "AR", TradingLedger.AccountsReceivable, customer.Id, OpenItemType.Invoice,
            LedgerSign.Debit, request.DocumentNumber, request.DocumentDate, customer.DueDateFor(request.DocumentDate),
            request.Currency, rate, gross, baseGross, request.Narrative, lines, request.SalesAccountCode, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<TradingPostingResult>> PostReceiptAsync(ReceiptRequest request, CancellationToken ct = default)
    {
        Customer? customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return Error.NotFound("Ar.CustomerNotFound", $"Customer {request.CustomerId} not found.");
        }

        Result<string> controlResult = await ControlCodeAsync(request.CompanyId, ControlAccountType.AccountsReceivable, ct).ConfigureAwait(false);
        if (controlResult.IsFailure)
        {
            return controlResult.Error;
        }

        Result<decimal> rateResult = await ResolveRateAsync(request.CompanyId, request.Currency, request.ExchangeRate, request.ReceiptDate, ct).ConfigureAwait(false);
        if (rateResult.IsFailure)
        {
            return rateResult.Error;
        }

        decimal rate = rateResult.Value;
        decimal baseAmount = Round(request.Amount * rate);

        List<JournalLineRequest> lines =
        [
            Line(request.BankAccountCode, JournalSide.Debit, request.Amount, rate, baseAmount),
            Line(controlResult.Value, JournalSide.Credit, request.Amount, rate, baseAmount),
        ];

        return await PostAndOpenAsync(request.CompanyId, "AR", TradingLedger.AccountsReceivable, customer.Id, OpenItemType.Receipt,
            LedgerSign.Credit, request.DocumentNumber, request.ReceiptDate, request.ReceiptDate, request.Currency, rate,
            request.Amount, baseAmount, request.Narrative, lines, request.BankAccountCode, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<TradingPostingResult>> PostCreditNoteAsync(SalesCreditNoteRequest request, CancellationToken ct = default)
    {
        Customer? customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return Error.NotFound("Ar.CustomerNotFound", $"Customer {request.CustomerId} not found.");
        }

        Result<string> controlResult = await ControlCodeAsync(request.CompanyId, ControlAccountType.AccountsReceivable, ct).ConfigureAwait(false);
        if (controlResult.IsFailure)
        {
            return controlResult.Error;
        }

        Result<decimal> rateResult = await ResolveRateAsync(request.CompanyId, request.Currency, request.ExchangeRate, request.DocumentDate, ct).ConfigureAwait(false);
        if (rateResult.IsFailure)
        {
            return rateResult.Error;
        }

        decimal rate = rateResult.Value;
        decimal gross = request.NetAmount + request.TaxAmount;
        decimal baseNet = Round(request.NetAmount * rate);
        decimal baseTax = Round(request.TaxAmount * rate);
        decimal baseGross = baseNet + baseTax;

        // Reverse of an invoice: Dr revenue, Dr output tax, Cr AR control.
        List<JournalLineRequest> lines =
        [
            Line(request.SalesAccountCode, JournalSide.Debit, request.NetAmount, rate, baseNet),
            Line(controlResult.Value, JournalSide.Credit, gross, rate, baseGross),
        ];
        if (request.TaxAmount != 0m && request.TaxAccountCode is not null)
        {
            lines.Add(Line(request.TaxAccountCode, JournalSide.Debit, request.TaxAmount, rate, baseTax));
        }

        return await PostAndOpenAsync(request.CompanyId, "AR", TradingLedger.AccountsReceivable, customer.Id, OpenItemType.CreditNote,
            LedgerSign.Credit, request.DocumentNumber, request.DocumentDate, request.DocumentDate, request.Currency, rate,
            gross, baseGross, request.Narrative, lines, request.SalesAccountCode, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<TradingPostingResult>> PostCreditNoteAsync(PurchaseCreditNoteRequest request, CancellationToken ct = default)
    {
        Supplier? supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId && s.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (supplier is null)
        {
            return Error.NotFound("Ap.SupplierNotFound", $"Supplier {request.SupplierId} not found.");
        }

        Result<string> controlResult = await ControlCodeAsync(request.CompanyId, ControlAccountType.AccountsPayable, ct).ConfigureAwait(false);
        if (controlResult.IsFailure)
        {
            return controlResult.Error;
        }

        Result<decimal> rateResult = await ResolveRateAsync(request.CompanyId, request.Currency, request.ExchangeRate, request.DocumentDate, ct).ConfigureAwait(false);
        if (rateResult.IsFailure)
        {
            return rateResult.Error;
        }

        decimal rate = rateResult.Value;
        decimal gross = request.NetAmount + request.TaxAmount;
        decimal baseNet = Round(request.NetAmount * rate);
        decimal baseTax = Round(request.TaxAmount * rate);
        decimal baseGross = baseNet + baseTax;

        // Reverse of a supplier invoice: Dr AP control, Cr expense, Cr input tax.
        List<JournalLineRequest> lines =
        [
            Line(controlResult.Value, JournalSide.Debit, gross, rate, baseGross),
            Line(request.ExpenseAccountCode, JournalSide.Credit, request.NetAmount, rate, baseNet),
        ];
        if (request.TaxAmount != 0m && request.TaxAccountCode is not null)
        {
            lines.Add(Line(request.TaxAccountCode, JournalSide.Credit, request.TaxAmount, rate, baseTax));
        }

        return await PostAndOpenAsync(request.CompanyId, "AP", TradingLedger.AccountsPayable, supplier.Id, OpenItemType.CreditNote,
            LedgerSign.Debit, request.DocumentNumber, request.DocumentDate, request.DocumentDate, request.Currency, rate,
            gross, baseGross, request.Narrative, lines, request.ExpenseAccountCode, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<TradingPostingResult>> PostInvoiceAsync(PurchaseInvoiceRequest request, CancellationToken ct = default)
    {
        Supplier? supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId && s.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (supplier is null)
        {
            return Error.NotFound("Ap.SupplierNotFound", $"Supplier {request.SupplierId} not found.");
        }

        Result<string> controlResult = await ControlCodeAsync(request.CompanyId, ControlAccountType.AccountsPayable, ct).ConfigureAwait(false);
        if (controlResult.IsFailure)
        {
            return controlResult.Error;
        }

        Result<decimal> rateResult = await ResolveRateAsync(request.CompanyId, request.Currency, request.ExchangeRate, request.DocumentDate, ct).ConfigureAwait(false);
        if (rateResult.IsFailure)
        {
            return rateResult.Error;
        }

        decimal rate = rateResult.Value;
        decimal gross = request.NetAmount + request.TaxAmount;
        decimal baseNet = Round(request.NetAmount * rate);
        decimal baseTax = Round(request.TaxAmount * rate);
        decimal baseGross = baseNet + baseTax;

        List<JournalLineRequest> lines =
        [
            Line(request.ExpenseAccountCode, JournalSide.Debit, request.NetAmount, rate, baseNet),
            Line(controlResult.Value, JournalSide.Credit, gross, rate, baseGross),
        ];
        if (request.TaxAmount != 0m && request.TaxAccountCode is not null)
        {
            lines.Add(Line(request.TaxAccountCode, JournalSide.Debit, request.TaxAmount, rate, baseTax));
        }

        return await PostAndOpenAsync(request.CompanyId, "AP", TradingLedger.AccountsPayable, supplier.Id, OpenItemType.Invoice,
            LedgerSign.Credit, request.DocumentNumber, request.DocumentDate, supplier.DueDateFor(request.DocumentDate),
            request.Currency, rate, gross, baseGross, request.Narrative, lines, request.ExpenseAccountCode, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<TradingPostingResult>> PostPaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        Supplier? supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId && s.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (supplier is null)
        {
            return Error.NotFound("Ap.SupplierNotFound", $"Supplier {request.SupplierId} not found.");
        }

        Result<string> controlResult = await ControlCodeAsync(request.CompanyId, ControlAccountType.AccountsPayable, ct).ConfigureAwait(false);
        if (controlResult.IsFailure)
        {
            return controlResult.Error;
        }

        Result<decimal> rateResult = await ResolveRateAsync(request.CompanyId, request.Currency, request.ExchangeRate, request.PaymentDate, ct).ConfigureAwait(false);
        if (rateResult.IsFailure)
        {
            return rateResult.Error;
        }

        decimal rate = rateResult.Value;
        decimal baseAmount = Round(request.Amount * rate);

        List<JournalLineRequest> lines =
        [
            Line(controlResult.Value, JournalSide.Debit, request.Amount, rate, baseAmount),
            Line(request.BankAccountCode, JournalSide.Credit, request.Amount, rate, baseAmount),
        ];

        return await PostAndOpenAsync(request.CompanyId, "AP", TradingLedger.AccountsPayable, supplier.Id, OpenItemType.Payment,
            LedgerSign.Debit, request.DocumentNumber, request.PaymentDate, request.PaymentDate, request.Currency, rate,
            request.Amount, baseAmount, request.Narrative, lines, request.BankAccountCode, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<TradingPostingResult>> PostReceiptReversalAsync(long companyId, long customerId, string documentNumber, DateOnly date, CurrencyCode currency, decimal amount, string bankAccountCode, decimal? exchangeRate = null, CancellationToken ct = default)
    {
        Customer? customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return Error.NotFound("Ar.CustomerNotFound", $"Customer {customerId} not found.");
        }

        Result<string> controlResult = await ControlCodeAsync(companyId, ControlAccountType.AccountsReceivable, ct).ConfigureAwait(false);
        if (controlResult.IsFailure)
        {
            return controlResult.Error;
        }

        Result<decimal> rateResult = await ResolveRateAsync(companyId, currency, exchangeRate, date, ct).ConfigureAwait(false);
        if (rateResult.IsFailure)
        {
            return rateResult.Error;
        }

        decimal rate = rateResult.Value;
        decimal baseAmount = Round(amount * rate);

        // The exact opposite of a receipt: Dr AR control, Cr bank/cash. Creates a debit adjustment item.
        List<JournalLineRequest> lines =
        [
            Line(controlResult.Value, JournalSide.Debit, amount, rate, baseAmount),
            Line(bankAccountCode, JournalSide.Credit, amount, rate, baseAmount),
        ];

        return await PostAndOpenAsync(companyId, "AR", TradingLedger.AccountsReceivable, customer.Id, OpenItemType.Journal,
            LedgerSign.Debit, documentNumber, date, date, currency, rate, amount, baseAmount, "Receipt reversal", lines, bankAccountCode, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<AllocationResult>> AllocateAsync(long companyId, TradingLedger ledger, long debitItemId, long creditItemId, decimal transactionAmount, DateOnly allocationDate, CancellationToken ct = default)
    {
        OpenItem? debit = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == debitItemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
        OpenItem? credit = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == creditItemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
        if (debit is null || credit is null)
        {
            return Error.NotFound("Alloc.ItemNotFound", "One or both open items were not found.");
        }

        if (debit.Sign != LedgerSign.Debit || credit.Sign != LedgerSign.Credit)
        {
            return Error.Validation("Alloc.WrongSides", "Allocation requires a debit item and a credit item.");
        }

        if (debit.TraderId != credit.TraderId || debit.Ledger != credit.Ledger || debit.Currency != credit.Currency)
        {
            return Error.Validation("Alloc.Mismatch", "Items must be the same trader, ledger and currency.");
        }

        decimal amount = Math.Min(transactionAmount, Math.Min(debit.OutstandingTransaction, credit.OutstandingTransaction));
        if (amount <= 0m)
        {
            return Error.Validation("Alloc.NothingToAllocate", "There is nothing outstanding to allocate.");
        }

        decimal debitBase = BaseDrawn(debit, amount);
        decimal creditBase = BaseDrawn(credit, amount);
        decimal realisedFx = decimal.Round(creditBase - debitBase, Scale, MidpointRounding.ToEven);

        debit.ReduceOutstanding(amount, debitBase);
        credit.ReduceOutstanding(amount, creditBase);

        string? fxJournalNumber = null;
        if (realisedFx != 0m)
        {
            Result<string> fxResult = await PostRealisedFxAsync(companyId, ledger, realisedFx, allocationDate, ct).ConfigureAwait(false);
            if (fxResult.IsFailure)
            {
                return fxResult.Error;
            }

            fxJournalNumber = fxResult.Value;
        }

        Allocation allocation = new(companyId, ledger, debit.TraderId, allocationDate, debit.Id, credit.Id, amount, debitBase, creditBase, realisedFx);
        _db.Allocations.Add(allocation);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new AllocationResult(allocation.Id, amount, realisedFx, fxJournalNumber);
    }

    private async Task<Result<string>> PostRealisedFxAsync(long companyId, TradingLedger ledger, decimal realisedFx, DateOnly date, CancellationToken ct, bool reverse = false)
    {
        ControlAccountType controlType = ledger == TradingLedger.AccountsReceivable ? ControlAccountType.AccountsReceivable : ControlAccountType.AccountsPayable;
        Result<string> controlResult = await ControlCodeAsync(companyId, controlType, ct).ConfigureAwait(false);
        if (controlResult.IsFailure)
        {
            return controlResult.Error;
        }

        // Reversal keeps the same FX account as the original (netting it to nil), only swapping the sides.
        ControlAccountType fxType = realisedFx > 0m ? ControlAccountType.FxGain : ControlAccountType.FxLoss;
        Result<string> fxResult = await ControlCodeAsync(companyId, fxType, ct).ConfigureAwait(false);
        if (fxResult.IsFailure)
        {
            return fxResult.Error;
        }

        Company company = await _db.Companies.FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        decimal magnitude = Math.Abs(realisedFx);

        // realisedFx > 0 (gain): Dr control, Cr FX gain. realisedFx < 0 (loss): Cr control, Dr FX loss.
        JournalSide controlSide = realisedFx > 0m ? JournalSide.Debit : JournalSide.Credit;
        JournalSide fxSide = realisedFx > 0m ? JournalSide.Credit : JournalSide.Debit;
        if (reverse)
        {
            (controlSide, fxSide) = (fxSide, controlSide);
        }

        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = ledger == TradingLedger.AccountsReceivable ? "AR" : "AP",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            EnforceManualJournalRules = false,
            DocumentType = reverse ? "RealisedFxReversal" : "RealisedFx",
            Narrative = reverse ? "Reversal of realised exchange difference" : "Realised exchange difference on allocation",
            Lines =
            [
                new JournalLineRequest { AccountCode = controlResult.Value, Side = controlSide, TransactionAmount = magnitude },
                new JournalLineRequest { AccountCode = fxResult.Value, Side = fxSide, TransactionAmount = magnitude },
            ],
        }, ct).ConfigureAwait(false);

        return posting.IsFailure ? posting.Error : posting.Value.JournalNumber;
    }

    /// <inheritdoc />
    public async Task<Result<UnallocationResult>> UnallocateAsync(long companyId, long allocationId, DateOnly reversalDate, CancellationToken ct = default)
    {
        Allocation? allocation = await _db.Allocations.FirstOrDefaultAsync(a => a.Id == allocationId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (allocation is null)
        {
            return Error.NotFound("Alloc.NotFound", $"Allocation {allocationId} not found.");
        }

        if (allocation.IsReversed)
        {
            return Error.Rule("Alloc.AlreadyReversed", $"Allocation {allocationId} has already been reversed.");
        }

        OpenItem? debit = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == allocation.DebitItemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
        OpenItem? credit = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == allocation.CreditItemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
        if (debit is null || credit is null)
        {
            return Error.NotFound("Alloc.ItemNotFound", "One or both open items were not found.");
        }

        // Restore both items to their pre-allocation state using the exact base amounts drawn.
        debit.RestoreOutstanding(allocation.TransactionAmount, allocation.DebitBase);
        credit.RestoreOutstanding(allocation.TransactionAmount, allocation.CreditBase);

        // Reverse any realised FX the allocation posted, netting the original gain/loss to nil.
        string? fxReversalJournal = null;
        if (allocation.RealisedFxBase != 0m)
        {
            Result<string> fxResult = await PostRealisedFxAsync(companyId, allocation.Ledger, allocation.RealisedFxBase, reversalDate, ct, reverse: true).ConfigureAwait(false);
            if (fxResult.IsFailure)
            {
                return fxResult.Error;
            }

            fxReversalJournal = fxResult.Value;
        }

        allocation.Reverse(reversalDate);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new UnallocationResult(allocation.Id, allocation.TransactionAmount, -allocation.RealisedFxBase, fxReversalJournal);
    }

    private async Task<Result<TradingPostingResult>> PostAndOpenAsync(
        long companyId, string module, TradingLedger ledger, long traderId, OpenItemType itemType, LedgerSign sign,
        string documentNumber, DateOnly documentDate, DateOnly dueDate, CurrencyCode currency, decimal rate,
        decimal amountTxn, decimal amountBase, string? narrative, List<JournalLineRequest> lines, string documentType, CancellationToken ct)
    {
        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = module,
            PostingDate = documentDate,
            TransactionCurrency = currency,
            EnforceManualJournalRules = false,
            DocumentType = documentType,
            DocumentNumber = documentNumber,
            Narrative = narrative,
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        OpenItem item = new(companyId, ledger, traderId, itemType, sign, documentNumber, documentDate, dueDate, currency, rate, amountTxn, amountBase)
        {
            JournalEntryId = posting.Value.JournalId,
            Narrative = narrative,
        };
        _db.OpenItems.Add(item);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new TradingPostingResult(item.Id, posting.Value.JournalNumber, amountBase);
    }

    private static decimal BaseDrawn(OpenItem item, decimal transactionAmount) =>
        transactionAmount >= item.OutstandingTransaction
            ? item.OutstandingBase
            : Round(transactionAmount * item.ExchangeRate);

    private async Task<Result<string>> ControlCodeAsync(long companyId, ControlAccountType type, CancellationToken ct)
    {
        string? code = await _db.Accounts
            .Where(a => a.CompanyId == companyId && a.ControlAccountType == type)
            .Select(a => a.AccountCode)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return code is null
            ? Error.Rule("Trading.NoControlAccount", $"No {type} control account is configured for company {companyId}.")
            : code;
    }

    private async Task<Result<decimal>> ResolveRateAsync(long companyId, CurrencyCode currency, decimal? explicitRate, DateOnly date, CancellationToken ct)
    {
        Company company = await _db.Companies.FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (currency == company.BaseCurrency)
        {
            return 1m;
        }

        if (explicitRate is { } r and > 0m)
        {
            return r;
        }

        ExchangeRate? rate = await _db.ExchangeRates
            .Where(x => x.FromCurrency == currency && x.ToCurrency == company.BaseCurrency && x.RateType == ExchangeRateType.Daily && x.EffectiveFrom <= date)
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (rate is null)
        {
            return Error.Rule("Trading.NoRate", $"No daily rate from {currency} to {company.BaseCurrency} on or before {date:yyyy-MM-dd}.");
        }

        return rate.Quotation == QuotationType.Direct ? rate.Rate : 1m / rate.Rate;
    }

    private static JournalLineRequest Line(string account, JournalSide side, decimal txn, decimal rate, decimal baseAmount) =>
        new() { AccountCode = account, Side = side, TransactionAmount = txn, ExchangeRate = rate, BaseAmount = baseAmount };

    private static decimal Round(decimal value) => decimal.Round(value, Scale, MidpointRounding.ToEven);
}
