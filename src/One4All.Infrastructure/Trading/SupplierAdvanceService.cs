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
/// Advances and prepayments to suppliers: paid into an advances asset account ahead of the invoice, applied to the
/// invoice when it arrives (moved into the purchase ledger as a payment item and allocated) or refunded. Every
/// posting goes through the central engine.
/// </summary>
public sealed class SupplierAdvanceService : ISupplierAdvanceService
{
    private const int Scale = 4;
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _postingEngine;
    private readonly IAllocationService _allocations;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">The central posting engine.</param>
    /// <param name="allocations">The sub-ledger allocation service.</param>
    public SupplierAdvanceService(One4AllDbContext db, IPostingEngine postingEngine, IAllocationService allocations)
    {
        _db = db;
        _postingEngine = postingEngine;
        _allocations = allocations;
    }

    /// <inheritdoc />
    public Task<Result<SupplierAdvanceResult>> PayAsync(SupplierAdvanceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _db.AtomicAsync(() => PayCoreAsync(request, ct), ct);
    }

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<SupplierAdvanceResult>> PayCoreAsync(SupplierAdvanceRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0m)
        {
            return Error.Validation("Ap.Advance.Amount", "An advance must be a positive amount.");
        }

        if (string.IsNullOrWhiteSpace(request.DocumentNumber) || string.IsNullOrWhiteSpace(request.AdvanceAccountCode) || string.IsNullOrWhiteSpace(request.BankAccountCode))
        {
            return Error.Validation("Ap.Advance.Fields", "An advance needs a document number, the advances account and the bank account.");
        }

        Supplier? supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId && s.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (supplier is null)
        {
            return Error.NotFound("Ap.SupplierNotFound", $"Supplier {request.SupplierId} not found.");
        }

        if (supplier.IsBlacklisted || supplier.OnHold)
        {
            return Error.Rule("Ap.Advance.SupplierHeld", $"Supplier {supplier.Code} is {(supplier.IsBlacklisted ? "blacklisted" : "on hold")}; no advance can be paid.");
        }

        foreach (string code in new[] { request.AdvanceAccountCode, request.BankAccountCode })
        {
            if (!await _db.Accounts.AnyAsync(a => a.CompanyId == request.CompanyId && a.AccountCode == code, ct).ConfigureAwait(false))
            {
                return Error.NotFound("Ap.Advance.Account", $"Ledger account '{code}' does not exist.");
            }
        }

        if (await _db.Set<SupplierAdvance>().AnyAsync(a => a.CompanyId == request.CompanyId && a.DocumentNumber == request.DocumentNumber, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Ap.Advance.Duplicate", $"Advance {request.DocumentNumber} already exists.");
        }

        Result<decimal> rate = await RateAsync(request.CompanyId, request.Currency, request.ExchangeRate, request.Date, ct).ConfigureAwait(false);
        if (rate.IsFailure)
        {
            return rate.Error;
        }

        decimal amountBase = Round(request.Amount * rate.Value);
        string narrative = request.Narrative ?? $"Advance to {supplier.Name}" + (request.PurchaseOrderNumber is null ? string.Empty : $" on {request.PurchaseOrderNumber}");
        Result<PostingReceipt> posting = await PostAsync(request.CompanyId, request.Date, request.Currency, "SupplierAdvance", request.DocumentNumber, narrative,
        [
            Line(request.AdvanceAccountCode, JournalSide.Debit, request.Amount, rate.Value, amountBase),
            Line(request.BankAccountCode, JournalSide.Credit, request.Amount, rate.Value, amountBase),
        ], ct).ConfigureAwait(false);
        if (posting.IsFailure)
        {
            return posting.Error;
        }

        SupplierAdvance advance = new(request.CompanyId, supplier.Id, request.DocumentNumber, request.Date, request.Currency, rate.Value, request.Amount, amountBase, request.AdvanceAccountCode, request.BankAccountCode)
        {
            PurchaseOrderNumber = request.PurchaseOrderNumber,
            Narrative = request.Narrative,
            JournalEntryId = posting.Value.JournalId,
        };
        _db.Set<SupplierAdvance>().Add(advance);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new SupplierAdvanceResult(advance.Id, posting.Value.JournalNumber, amountBase);
    }

    /// <inheritdoc />
    public Task<Result<SupplierAdvanceApplication>> ApplyAsync(long companyId, long advanceId, long invoiceOpenItemId, DateOnly date, decimal? amount = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => ApplyCoreAsync(companyId, advanceId, invoiceOpenItemId, date, amount, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<SupplierAdvanceApplication>> ApplyCoreAsync(long companyId, long advanceId, long invoiceOpenItemId, DateOnly date, decimal? amount, CancellationToken ct)
    {
        SupplierAdvance? advance = await _db.Set<SupplierAdvance>().FirstOrDefaultAsync(a => a.Id == advanceId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (advance is null)
        {
            return Error.NotFound("Ap.Advance.NotFound", $"Advance {advanceId} not found.");
        }

        if (advance.Outstanding <= 0m)
        {
            return Error.Rule("Ap.Advance.Spent", $"Advance {advance.DocumentNumber} has nothing left to apply.");
        }

        OpenItem? invoice = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == invoiceOpenItemId && i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable, ct).ConfigureAwait(false);
        if (invoice is null)
        {
            return Error.NotFound("Ap.OpenItemNotFound", $"AP open item {invoiceOpenItemId} not found.");
        }

        if (invoice.TraderId != advance.SupplierId)
        {
            return Error.Rule("Ap.Advance.OtherSupplier", $"Invoice {invoice.DocumentNumber} belongs to another supplier than advance {advance.DocumentNumber}.");
        }

        if (invoice.Sign != LedgerSign.Credit || invoice.OutstandingTransaction <= 0m)
        {
            return Error.Rule("Ap.Advance.NotAnInvoice", $"{invoice.DocumentNumber} is not an outstanding creditor item.");
        }

        if (invoice.Currency != advance.Currency)
        {
            return Error.Rule("Ap.Advance.Currency", $"Advance {advance.DocumentNumber} is in {advance.Currency.Value}; invoice {invoice.DocumentNumber} is in {invoice.Currency.Value}.");
        }

        if (date < advance.AdvanceDate)
        {
            return Error.Validation("Ap.Advance.Date", "An advance is applied on or after the day it was paid.");
        }

        decimal applied = amount ?? Math.Min(advance.Outstanding, invoice.OutstandingTransaction);
        if (applied <= 0m || applied > advance.Outstanding || applied > invoice.OutstandingTransaction)
        {
            return Error.Validation("Ap.Advance.ApplyAmount", $"The amount must be positive and within both the advance's balance ({advance.Outstanding:N2}) and the invoice's ({invoice.OutstandingTransaction:N2}).");
        }

        Result<string> control = await ControlCodeAsync(companyId, ct).ConfigureAwait(false);
        if (control.IsFailure)
        {
            return control.Error;
        }

        // The advance moves into the purchase ledger at the rate it was paid at: the advances account clears exactly, and any
        // difference against the invoice's rate is realised when the payment item is allocated.
        decimal appliedBase = applied >= advance.Outstanding ? advance.AmountBase - Round(advance.Applied * advance.ExchangeRate) - Round(advance.Refunded * advance.ExchangeRate) : Round(applied * advance.ExchangeRate);
        int sequence = advance.Apply(applied);
        string document = $"{advance.DocumentNumber}/A{sequence}";
        Result<PostingReceipt> posting = await PostAsync(companyId, date, advance.Currency, "SupplierPayment", document, $"Advance {advance.DocumentNumber} applied to {invoice.DocumentNumber}",
        [
            Line(control.Value, JournalSide.Debit, applied, advance.ExchangeRate, appliedBase),
            Line(advance.AdvanceAccountCode, JournalSide.Credit, applied, advance.ExchangeRate, appliedBase),
        ], ct).ConfigureAwait(false);
        if (posting.IsFailure)
        {
            return posting.Error;
        }

        OpenItem payment = new(companyId, TradingLedger.AccountsPayable, advance.SupplierId, OpenItemType.Payment, LedgerSign.Debit, document, date, date, advance.Currency, advance.ExchangeRate, applied, appliedBase)
        {
            JournalEntryId = posting.Value.JournalId,
            Narrative = $"Advance {advance.DocumentNumber} applied",
        };
        _db.OpenItems.Add(payment);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        Result<AllocationResult> allocation = await _allocations.AllocateAsync(companyId, TradingLedger.AccountsPayable, payment.Id, invoice.Id, applied, date, ct).ConfigureAwait(false);
        if (allocation.IsFailure)
        {
            return allocation.Error;
        }

        await _db.Entry(invoice).ReloadAsync(ct).ConfigureAwait(false);
        return new SupplierAdvanceApplication(applied, payment.Id, posting.Value.JournalNumber, advance.Outstanding, invoice.OutstandingTransaction);
    }

    /// <inheritdoc />
    public Task<Result<string>> RefundAsync(long companyId, long advanceId, DateOnly date, string bankAccountCode, string documentNumber, decimal? amount = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => RefundCoreAsync(companyId, advanceId, date, bankAccountCode, documentNumber, amount, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<string>> RefundCoreAsync(long companyId, long advanceId, DateOnly date, string bankAccountCode, string documentNumber, decimal? amount, CancellationToken ct)
    {
        SupplierAdvance? advance = await _db.Set<SupplierAdvance>().FirstOrDefaultAsync(a => a.Id == advanceId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (advance is null)
        {
            return Error.NotFound("Ap.Advance.NotFound", $"Advance {advanceId} not found.");
        }

        if (advance.Outstanding <= 0m)
        {
            return Error.Rule("Ap.Advance.Spent", $"Advance {advance.DocumentNumber} has no balance to refund.");
        }

        if (string.IsNullOrWhiteSpace(bankAccountCode) || string.IsNullOrWhiteSpace(documentNumber))
        {
            return Error.Validation("Ap.Advance.Fields", "A refund needs the bank account and a document number.");
        }

        decimal refunded = amount ?? advance.Outstanding;
        if (refunded <= 0m || refunded > advance.Outstanding)
        {
            return Error.Validation("Ap.Advance.RefundAmount", $"The refund must be positive and within the advance's balance ({advance.Outstanding:N2}).");
        }

        decimal refundedBase = refunded >= advance.Outstanding ? advance.AmountBase - Round(advance.Applied * advance.ExchangeRate) - Round(advance.Refunded * advance.ExchangeRate) : Round(refunded * advance.ExchangeRate);
        advance.Refund(refunded);
        Result<PostingReceipt> posting = await PostAsync(companyId, date, advance.Currency, "SupplierAdvanceRefund", documentNumber, $"Advance {advance.DocumentNumber} refunded",
        [
            Line(bankAccountCode, JournalSide.Debit, refunded, advance.ExchangeRate, refundedBase),
            Line(advance.AdvanceAccountCode, JournalSide.Credit, refunded, advance.ExchangeRate, refundedBase),
        ], ct).ConfigureAwait(false);
        if (posting.IsFailure)
        {
            return posting.Error;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return posting.Value.JournalNumber;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupplierAdvanceRow>> ListAsync(long companyId, long? supplierId = null, bool openOnly = false, CancellationToken ct = default)
    {
        List<SupplierAdvance> advances = await _db.Set<SupplierAdvance>().AsNoTracking()
            .Where(a => a.CompanyId == companyId && (supplierId == null || a.SupplierId == supplierId) && (!openOnly || a.Status == SupplierAdvanceStatus.Open))
            .OrderByDescending(a => a.AdvanceDate).ThenByDescending(a => a.Id).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> suppliers = await _db.Suppliers.AsNoTracking().IgnoreQueryFilters([One4AllDbContext.SoftDeleteFilter]).Where(s => s.CompanyId == companyId)
            .ToDictionaryAsync(s => s.Id, s => s.Code, ct).ConfigureAwait(false);
        return [.. advances.Select(a => new SupplierAdvanceRow(a.Id, a.SupplierId, suppliers.GetValueOrDefault(a.SupplierId, "?"), a.DocumentNumber, a.AdvanceDate, a.Currency.Value, a.Amount, a.Applied, a.Refunded,
            a.Outstanding, a.Status, a.PurchaseOrderNumber))];
    }

    private async Task<Result<decimal>> RateAsync(long companyId, CurrencyCode currency, decimal? explicitRate, DateOnly date, CancellationToken ct)
    {
        Company company = await _db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (currency == company.BaseCurrency)
        {
            return 1m;
        }

        if (explicitRate is { } keyed and > 0m)
        {
            return keyed;
        }

        decimal? table = await Accounting.ExchangeRateResolver.FindAsync(_db, currency, company.BaseCurrency, ExchangeRateType.Daily, date, ct).ConfigureAwait(false);
        return table is decimal rate ? rate : Error.Rule("Trading.NoExchangeRate", $"No {currency.Value} rate to {company.BaseCurrency.Value} is on file for {date:yyyy-MM-dd}.");
    }

    private async Task<Result<string>> ControlCodeAsync(long companyId, CancellationToken ct)
    {
        string? code = await _db.Accounts.Where(a => a.CompanyId == companyId && a.ControlAccountType == ControlAccountType.AccountsPayable).OrderBy(a => a.AccountCode)
            .Select(a => a.AccountCode).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return code is null ? Error.Rule("Trading.NoControlAccount", $"No AccountsPayable control account is configured for company {companyId}.") : code;
    }

    private Task<Result<PostingReceipt>> PostAsync(long companyId, DateOnly date, CurrencyCode currency, string documentType, string documentNumber, string narrative, List<JournalLineRequest> lines, CancellationToken ct)
        => _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "AP",
            PostingDate = date,
            TransactionCurrency = currency,
            EnforceManualJournalRules = false,
            DocumentType = documentType,
            DocumentNumber = documentNumber,
            Narrative = narrative,
            Lines = lines,
        }, ct);

    private static JournalLineRequest Line(string accountCode, JournalSide side, decimal transactionAmount, decimal rate, decimal baseAmount)
        => new() { AccountCode = accountCode, Side = side, TransactionAmount = transactionAmount, ExchangeRate = rate, BaseAmount = baseAmount };

    private static decimal Round(decimal value) => decimal.Round(value, Scale, MidpointRounding.ToEven);
}
