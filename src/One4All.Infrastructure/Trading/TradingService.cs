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

        if (request.RevenueLines.Count > 0
            && (request.RevenueLines.Sum(r => r.NetAmount) != request.NetAmount || request.RevenueLines.Any(r => string.IsNullOrWhiteSpace(r.AccountCode))))
        {
            return Error.Validation("Ar.RevenueLinesMismatch", "The revenue lines must each name an account and add up to the net amount.");
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

        List<JournalLineRequest> lines = [Line(controlResult.Value, JournalSide.Debit, gross, rate, baseGross)];
        if (request.RevenueLines.Count == 0)
        {
            lines.Add(Line(request.SalesAccountCode, JournalSide.Credit, request.NetAmount, rate, baseNet));
        }
        else
        {
            // Each revenue account gets its share; the last absorbs base rounding so the credits equal the base net.
            decimal baseLeft = baseNet;
            for (int i = 0; i < request.RevenueLines.Count; i++)
            {
                RevenueLine revenue = request.RevenueLines[i];
                decimal baseShare = i == request.RevenueLines.Count - 1 ? baseLeft : Round(revenue.NetAmount * rate);
                baseLeft -= baseShare;
                lines.Add(Line(revenue.AccountCode, JournalSide.Credit, revenue.NetAmount, rate, baseShare));
            }
        }

        if (request.TaxAmount != 0m && request.TaxAccountCode is not null)
        {
            lines.Add(Line(request.TaxAccountCode, JournalSide.Credit, request.TaxAmount, rate, baseTax, request.TaxCode));
        }

        return await PostAndOpenAsync(request.CompanyId, "AR", TradingLedger.AccountsReceivable, customer.Id, OpenItemType.Invoice,
            LedgerSign.Debit, request.DocumentNumber, request.DocumentDate, customer.DueDateFor(request.DocumentDate),
            request.Currency, rate, gross, baseGross, request.Narrative, lines, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> ContraAsync(long companyId, long customerId, long customerInvoiceItemId, long supplierId, long supplierInvoiceItemId, decimal amount, DateOnly date, string contraClearingAccountCode, string documentNumber, CancellationToken ct = default)
    {
        if (amount <= 0m)
        {
            return Result.Failure(Error.Rule("Ar.ContraNonPositive", "The contra amount must be positive."));
        }

        if (string.IsNullOrWhiteSpace(contraClearingAccountCode))
        {
            return Result.Failure(Error.Validation("Ar.NoContraAccount", "A contra needs a clearing account."));
        }

        OpenItem? arItem = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == customerInvoiceItemId && i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customerId, ct).ConfigureAwait(false);
        OpenItem? apItem = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == supplierInvoiceItemId && i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable && i.TraderId == supplierId, ct).ConfigureAwait(false);
        if (arItem is null || apItem is null)
        {
            return Result.Failure(Error.NotFound("Ar.ContraItemNotFound", "The customer or supplier invoice open item was not found."));
        }

        if (amount > arItem.OutstandingTransaction || amount > apItem.OutstandingTransaction)
        {
            return Result.Failure(Error.Rule("Ar.ContraTooLarge", $"The contra {amount:0.00} exceeds an item's outstanding (AR {arItem.OutstandingTransaction:0.00}, AP {apItem.OutstandingTransaction:0.00})."));
        }

        // AR leg: a receipt to the clearing account settles the debtor invoice.
        Result<TradingPostingResult> arReceipt = await PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = customerId, DocumentNumber = documentNumber, ReceiptDate = date,
            Currency = arItem.Currency, Amount = amount, BankAccountCode = contraClearingAccountCode, Narrative = "Contra settlement",
        }, ct).ConfigureAwait(false);
        if (arReceipt.IsFailure)
        {
            return Result.Failure(arReceipt.Error);
        }

        Result<AllocationResult> arAlloc = await AllocateAsync(companyId, TradingLedger.AccountsReceivable, customerInvoiceItemId, arReceipt.Value.OpenItemId, amount, date, ct).ConfigureAwait(false);
        if (arAlloc.IsFailure)
        {
            return Result.Failure(arAlloc.Error);
        }

        // AP leg: a payment from the clearing account settles the creditor invoice (clearing nets to zero).
        Result<TradingPostingResult> apPayment = await PostPaymentAsync(new PaymentRequest
        {
            CompanyId = companyId, SupplierId = supplierId, DocumentNumber = documentNumber, PaymentDate = date,
            Currency = apItem.Currency, Amount = amount, BankAccountCode = contraClearingAccountCode, Narrative = "Contra settlement",
        }, ct).ConfigureAwait(false);
        if (apPayment.IsFailure)
        {
            return Result.Failure(apPayment.Error);
        }

        Result<AllocationResult> apAlloc = await AllocateAsync(companyId, TradingLedger.AccountsPayable, apPayment.Value.OpenItemId, supplierInvoiceItemId, amount, date, ct).ConfigureAwait(false);
        return apAlloc.IsFailure ? Result.Failure(apAlloc.Error) : Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<TradingPostingResult>> WriteOffAsync(long companyId, long openItemId, DateOnly date, string badDebtAccountCode, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Ar.ReasonRequired", "A bad-debt write-off requires a reason.");
        }

        if (string.IsNullOrWhiteSpace(badDebtAccountCode))
        {
            return Error.Validation("Ar.NoBadDebtAccount", "A bad-debt write-off requires a bad-debt account.");
        }

        OpenItem? item = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == openItemId && i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound("Ar.OpenItemNotFound", $"AR open item {openItemId} not found.");
        }

        if (item.Sign != LedgerSign.Debit || item.OutstandingTransaction <= 0m)
        {
            return Error.Rule("Ar.NothingToWriteOff", "Only an outstanding debtor item (invoice) can be written off.");
        }

        // Post the write-off as a "receipt" whose money side is the bad-debt expense (Dr bad debt / Cr AR control).
        Result<TradingPostingResult> writeOff = await PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId,
            CustomerId = item.TraderId,
            DocumentNumber = $"WO{openItemId}",
            ReceiptDate = date,
            Currency = item.Currency,
            Amount = item.OutstandingTransaction,
            BankAccountCode = badDebtAccountCode,
            ExchangeRate = item.ExchangeRate,
            Narrative = $"Bad-debt write-off of {item.DocumentNumber}: {reason}",
        }, ct).ConfigureAwait(false);

        if (writeOff.IsFailure)
        {
            return writeOff.Error;
        }

        // Allocate the write-off against the invoice so it settles and stops ageing.
        Result<AllocationResult> allocation = await AllocateAsync(companyId, TradingLedger.AccountsReceivable, openItemId, writeOff.Value.OpenItemId, item.OutstandingTransaction, date, ct).ConfigureAwait(false);
        if (allocation.IsFailure)
        {
            return allocation.Error;
        }

        return writeOff.Value;
    }

    /// <inheritdoc />
    public async Task<Result<TradingPostingResult>> WriteBackAsync(long companyId, long openItemId, DateOnly date, string writeBackAccountCode, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Ap.ReasonRequired", "A creditor write-back requires a reason.");
        }

        if (string.IsNullOrWhiteSpace(writeBackAccountCode))
        {
            return Error.Validation("Ap.NoWriteBackAccount", "A creditor write-back requires a write-back income account.");
        }

        OpenItem? item = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == openItemId && i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound("Ap.OpenItemNotFound", $"AP open item {openItemId} not found.");
        }

        if (item.Sign != LedgerSign.Credit || item.OutstandingTransaction <= 0m)
        {
            return Error.Rule("Ap.NothingToWriteBack", "Only an outstanding creditor item (invoice) can be written back.");
        }

        // Post the write-back as a "payment" whose money side is the write-back income (Dr AP control / Cr income).
        Result<TradingPostingResult> writeBack = await PostPaymentAsync(new PaymentRequest
        {
            CompanyId = companyId,
            SupplierId = item.TraderId,
            DocumentNumber = $"WB{openItemId}",
            PaymentDate = date,
            Currency = item.Currency,
            Amount = item.OutstandingTransaction,
            BankAccountCode = writeBackAccountCode,
            ExchangeRate = item.ExchangeRate,
            Narrative = $"Creditor write-back of {item.DocumentNumber}: {reason}",
        }, ct).ConfigureAwait(false);

        if (writeBack.IsFailure)
        {
            return writeBack.Error;
        }

        // On AP the payment is the debit item, the invoice the credit item; allocate so the invoice settles.
        Result<AllocationResult> allocation = await AllocateAsync(companyId, TradingLedger.AccountsPayable, writeBack.Value.OpenItemId, openItemId, item.OutstandingTransaction, date, ct).ConfigureAwait(false);
        if (allocation.IsFailure)
        {
            return allocation.Error;
        }

        return writeBack.Value;
    }

    /// <inheritdoc />
    public async Task<Result<TradingPostingResult>> SettleWithDiscountAsync(long companyId, long invoiceOpenItemId, DateOnly date, decimal cashAmount, decimal discountAmount, string bankAccountCode, string discountAccountCode, string documentNumber, CancellationToken ct = default)
    {
        if (cashAmount <= 0m || discountAmount <= 0m)
        {
            return Error.Rule("Ar.SettlementNonPositive", "The cash and discount amounts must both be positive.");
        }

        OpenItem? invoice = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == invoiceOpenItemId && i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable, ct).ConfigureAwait(false);
        if (invoice is null)
        {
            return Error.NotFound("Ar.OpenItemNotFound", $"AR open item {invoiceOpenItemId} not found.");
        }

        if (invoice.Sign != LedgerSign.Debit || invoice.OutstandingTransaction <= 0m)
        {
            return Error.Rule("Ar.NothingToSettle", "Only an outstanding debtor item (invoice) can be settled.");
        }

        if (cashAmount + discountAmount > invoice.OutstandingTransaction)
        {
            return Error.Rule("Ar.SettlementExceedsOutstanding", $"The cash {cashAmount:0.00} plus discount {discountAmount:0.00} exceeds the outstanding {invoice.OutstandingTransaction:0.00}.");
        }

        // Cash receipt: Dr bank / Cr AR control.
        Result<TradingPostingResult> cash = await PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = invoice.TraderId, DocumentNumber = documentNumber, ReceiptDate = date,
            Currency = invoice.Currency, Amount = cashAmount, BankAccountCode = bankAccountCode, ExchangeRate = invoice.ExchangeRate,
            Narrative = $"Receipt with settlement discount for {invoice.DocumentNumber}",
        }, ct).ConfigureAwait(false);
        if (cash.IsFailure)
        {
            return cash.Error;
        }

        Result<AllocationResult> allocCash = await AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoiceOpenItemId, cash.Value.OpenItemId, cashAmount, date, ct).ConfigureAwait(false);
        if (allocCash.IsFailure)
        {
            return allocCash.Error;
        }

        // Discount: posted as a "receipt" whose money side is the discounts-allowed account (Dr discount / Cr AR).
        Result<TradingPostingResult> discount = await PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = invoice.TraderId, DocumentNumber = $"{documentNumber}-DISC", ReceiptDate = date,
            Currency = invoice.Currency, Amount = discountAmount, BankAccountCode = discountAccountCode, ExchangeRate = invoice.ExchangeRate,
            Narrative = $"Settlement discount for {invoice.DocumentNumber}",
        }, ct).ConfigureAwait(false);
        if (discount.IsFailure)
        {
            return discount.Error;
        }

        Result<AllocationResult> allocDiscount = await AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoiceOpenItemId, discount.Value.OpenItemId, discountAmount, date, ct).ConfigureAwait(false);
        if (allocDiscount.IsFailure)
        {
            return allocDiscount.Error;
        }

        return cash.Value;
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
            request.Amount, baseAmount, request.Narrative, lines, ct).ConfigureAwait(false);
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
            gross, baseGross, request.Narrative, lines, ct).ConfigureAwait(false);
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
            gross, baseGross, request.Narrative, lines, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<TradingPostingResult>> PostInvoiceAsync(PurchaseInvoiceRequest request, CancellationToken ct = default)
    {
        Supplier? supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId && s.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (supplier is null)
        {
            return Error.NotFound("Ap.SupplierNotFound", $"Supplier {request.SupplierId} not found.");
        }

        // Duplicate-invoice control: the same supplier invoice entered twice is the classic double payment.
        // Numbers compare on letters and digits only, so "INV-001", "inv 001" and "INV001" are one invoice.
        if (string.IsNullOrWhiteSpace(request.DocumentNumber))
        {
            return Error.Validation("Ap.InvoiceNumberRequired", "A supplier invoice needs the supplier's invoice number.");
        }

        List<string> existing = await _db.OpenItems
            .Where(i => i.CompanyId == request.CompanyId && i.Ledger == TradingLedger.AccountsPayable && i.TraderId == supplier.Id && i.ItemType == OpenItemType.Invoice)
            .Select(i => i.DocumentNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        string? clash = existing.FirstOrDefault(n => One4All.Application.Trading.DocumentNumbers.Same(n, request.DocumentNumber));
        if (clash is not null)
        {
            return Error.Conflict("Ap.DuplicateInvoice", $"Supplier {supplier.Code} invoice {request.DocumentNumber} has already been entered (as {clash}).");
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
            lines.Add(Line(request.TaxAccountCode, JournalSide.Debit, request.TaxAmount, rate, baseTax, request.TaxCode));
        }

        return await PostAndOpenAsync(request.CompanyId, "AP", TradingLedger.AccountsPayable, supplier.Id, OpenItemType.Invoice,
            LedgerSign.Credit, request.DocumentNumber, request.DocumentDate, supplier.DueDateFor(request.DocumentDate),
            request.Currency, rate, gross, baseGross, request.Narrative, lines, ct).ConfigureAwait(false);
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
            request.Amount, baseAmount, request.Narrative, lines, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<TradingPostingResult>> PayWithDiscountAsync(long companyId, long invoiceOpenItemId, DateOnly date, decimal cashAmount, decimal discountAmount, string bankAccountCode, string discountReceivedAccountCode, string documentNumber, CancellationToken ct = default)
    {
        if (cashAmount <= 0m || discountAmount <= 0m)
        {
            return Error.Rule("Ap.SettlementNonPositive", "The cash and discount amounts must both be positive.");
        }

        OpenItem? invoice = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == invoiceOpenItemId && i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable, ct).ConfigureAwait(false);
        if (invoice is null)
        {
            return Error.NotFound("Ap.OpenItemNotFound", $"AP open item {invoiceOpenItemId} not found.");
        }

        if (invoice.Sign != LedgerSign.Credit || invoice.OutstandingTransaction <= 0m)
        {
            return Error.Rule("Ap.NothingToSettle", "Only an outstanding creditor item (invoice) can be settled.");
        }

        if (cashAmount + discountAmount > invoice.OutstandingTransaction)
        {
            return Error.Rule("Ap.SettlementExceedsOutstanding", $"The cash {cashAmount:0.00} plus discount {discountAmount:0.00} exceeds the outstanding {invoice.OutstandingTransaction:0.00}.");
        }

        // Cash payment: Dr AP control / Cr bank.
        Result<TradingPostingResult> cash = await PostPaymentAsync(new PaymentRequest
        {
            CompanyId = companyId, SupplierId = invoice.TraderId, DocumentNumber = documentNumber, PaymentDate = date,
            Currency = invoice.Currency, Amount = cashAmount, BankAccountCode = bankAccountCode, ExchangeRate = invoice.ExchangeRate,
            Narrative = $"Payment with settlement discount for {invoice.DocumentNumber}",
        }, ct).ConfigureAwait(false);
        if (cash.IsFailure)
        {
            return cash.Error;
        }

        Result<AllocationResult> allocCash = await AllocateAsync(companyId, TradingLedger.AccountsPayable, cash.Value.OpenItemId, invoiceOpenItemId, cashAmount, date, ct).ConfigureAwait(false);
        if (allocCash.IsFailure)
        {
            return allocCash.Error;
        }

        // Discount: posted as a "payment" whose money side is the discounts-received income (Dr AP / Cr income).
        Result<TradingPostingResult> discount = await PostPaymentAsync(new PaymentRequest
        {
            CompanyId = companyId, SupplierId = invoice.TraderId, DocumentNumber = $"{documentNumber}-DISC", PaymentDate = date,
            Currency = invoice.Currency, Amount = discountAmount, BankAccountCode = discountReceivedAccountCode, ExchangeRate = invoice.ExchangeRate,
            Narrative = $"Settlement discount received for {invoice.DocumentNumber}",
        }, ct).ConfigureAwait(false);
        if (discount.IsFailure)
        {
            return discount.Error;
        }

        Result<AllocationResult> allocDiscount = await AllocateAsync(companyId, TradingLedger.AccountsPayable, discount.Value.OpenItemId, invoiceOpenItemId, discountAmount, date, ct).ConfigureAwait(false);
        if (allocDiscount.IsFailure)
        {
            return allocDiscount.Error;
        }

        return cash.Value;
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
            LedgerSign.Debit, documentNumber, date, date, currency, rate, amount, baseAmount, "Receipt reversal", lines, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<AllocationResult>> AllocateAsync(long companyId, TradingLedger ledger, long debitItemId, long creditItemId, decimal transactionAmount, DateOnly allocationDate, CancellationToken ct = default)
        => _db.AtomicAsync(() => AllocateCoreAsync(companyId, ledger, debitItemId, creditItemId, transactionAmount, allocationDate, ct), ct);

    // One unit of work, retried if another user allocates against the same items at the same moment.
    private async Task<Result<AllocationResult>> AllocateCoreAsync(long companyId, TradingLedger ledger, long debitItemId, long creditItemId, decimal transactionAmount, DateOnly allocationDate, CancellationToken ct)
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

        // Post the realised FX before touching the items; the FX journal and the allocation commit together (the
        // public method runs this as one unit of work, retried if another user changed either item meanwhile).
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

        debit.ReduceOutstanding(amount, debitBase);
        credit.ReduceOutstanding(amount, creditBase);
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
    public Task<Result<UnallocationResult>> UnallocateAsync(long companyId, long allocationId, DateOnly reversalDate, CancellationToken ct = default)
        => _db.AtomicAsync(() => UnallocateCoreAsync(companyId, allocationId, reversalDate, ct), ct);

    // One unit of work, retried if another user allocates against the same items at the same moment.
    private async Task<Result<UnallocationResult>> UnallocateCoreAsync(long companyId, long allocationId, DateOnly reversalDate, CancellationToken ct)
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

        // Reverse any realised FX the allocation posted (netting the original gain/loss to nil) before touching the
        // items, and commit the reversal journal with the restored items.
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

        // Restore both items to their pre-allocation state using the exact base amounts drawn.
        debit.RestoreOutstanding(allocation.TransactionAmount, allocation.DebitBase);
        credit.RestoreOutstanding(allocation.TransactionAmount, allocation.CreditBase);
        allocation.Reverse(reversalDate);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new UnallocationResult(allocation.Id, allocation.TransactionAmount, -allocation.RealisedFxBase, fxReversalJournal);
    }

    private async Task<Result<TradingPostingResult>> PostAndOpenAsync(
        long companyId, string module, TradingLedger ledger, long traderId, OpenItemType itemType, LedgerSign sign,
        string documentNumber, DateOnly documentDate, DateOnly dueDate, CurrencyCode currency, decimal rate,
        decimal amountTxn, decimal amountBase, string? narrative, List<JournalLineRequest> lines, CancellationToken ct)
    {
        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = module,
            PostingDate = documentDate,
            TransactionCurrency = currency,
            EnforceManualJournalRules = false,
            DocumentType = DocumentTypeFor(ledger, itemType),
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

    /// <summary>
    /// The journal document type of a trading document — what the account enquiry, the audit trail and journal
    /// listings show for it. (These journals used to carry an account code here.)
    /// </summary>
    /// <param name="ledger">The sub-ledger.</param>
    /// <param name="itemType">The open-item type the document raises.</param>
    /// <returns>The document type.</returns>
    internal static string DocumentTypeFor(TradingLedger ledger, OpenItemType itemType) => (ledger, itemType) switch
    {
        (TradingLedger.AccountsReceivable, OpenItemType.Invoice) => "SalesInvoice",
        (TradingLedger.AccountsReceivable, OpenItemType.CreditNote) => "SalesCreditNote",
        (TradingLedger.AccountsReceivable, OpenItemType.Receipt) => "CustomerReceipt",
        (TradingLedger.AccountsReceivable, OpenItemType.Journal) => "ReceiptReversal",
        (TradingLedger.AccountsPayable, OpenItemType.Invoice) => "PurchaseInvoice",
        (TradingLedger.AccountsPayable, OpenItemType.CreditNote) => "PurchaseCreditNote",
        (TradingLedger.AccountsPayable, OpenItemType.Payment) => "SupplierPayment",
        _ => $"{(ledger == TradingLedger.AccountsReceivable ? "AR" : "AP")}{itemType}",
    };

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

    private static JournalLineRequest Line(string account, JournalSide side, decimal txn, decimal rate, decimal baseAmount, string? taxCode = null) =>
        new() { AccountCode = account, Side = side, TransactionAmount = txn, ExchangeRate = rate, BaseAmount = baseAmount, TaxCode = taxCode };

    private static decimal Round(decimal value) => decimal.Round(value, Scale, MidpointRounding.ToEven);
}
