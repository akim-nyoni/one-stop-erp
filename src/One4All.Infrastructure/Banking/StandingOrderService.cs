using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Banking;
using One4All.Core.Accounting;
using One4All.Core.Banking;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Banking;

/// <summary>Standing orders and direct debit collections (brief §4.5, §4.6).</summary>
public sealed class StandingOrderService : IStandingOrderService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _engine;
    private readonly ISalesLedgerService _receivables;
    private readonly IPurchaseLedgerService _payables;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="engine">Posts GL standing orders.</param>
    /// <param name="receivables">Receipts direct debits.</param>
    /// <param name="payables">Pays supplier standing orders.</param>
    public StandingOrderService(One4AllDbContext db, IPostingEngine engine, ISalesLedgerService receivables, IPurchaseLedgerService payables)
    {
        _db = db;
        _engine = engine;
        _receivables = receivables;
        _payables = payables;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateStandingOrderAsync(StandingOrderRequest request, CancellationToken ct = default)
    {
        string reference = (request.Reference ?? string.Empty).Trim().ToUpperInvariant();
        bool toSupplier = request.SupplierId is not null, toAccount = !string.IsNullOrWhiteSpace(request.DebitAccountCode);
        if (reference.Length == 0 || request.Amount <= 0m || toSupplier == toAccount || string.IsNullOrWhiteSpace(request.BankAccountCode) || request.EndDate < request.StartDate)
        {
            return Error.Validation("Cb.StandingOrderInvalid", "A standing order needs a reference, a bank, a positive amount, and either a supplier or a GL account.");
        }

        if (await ReferenceTakenAsync(request.CompanyId, reference, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Cb.ReferenceTaken", $"Reference {reference} is already used.");
        }

        if (request.SupplierId is long supplierId && !await _db.Suppliers.AnyAsync(s => s.Id == supplierId && s.CompanyId == request.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Cb.SupplierNotFound", $"Supplier {supplierId} not found.");
        }

        StandingOrder order = new(request.CompanyId, reference, request.BankAccountCode.Trim(), request.Amount, request.Frequency, request.StartDate, request.EndDate, request.SupplierId,
            toAccount ? request.DebitAccountCode!.Trim() : null, string.IsNullOrWhiteSpace(request.Narrative) ? reference : request.Narrative.Trim());
        _db.Add(order);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return order.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateMandateAsync(DirectDebitMandateRequest request, CancellationToken ct = default)
    {
        string reference = (request.Reference ?? string.Empty).Trim().ToUpperInvariant();
        if (reference.Length == 0 || string.IsNullOrWhiteSpace(request.BankAccountCode) || string.IsNullOrWhiteSpace(request.CustomerBankAccount) || request.FixedAmount <= 0m
            || request.MaximumAmount <= 0m || request.EndDate < request.StartDate)
        {
            return Error.Validation("Cb.MandateInvalid", "A mandate needs a reference, the customer's bank account, a bank to collect into, and positive amounts if given.");
        }

        if (await ReferenceTakenAsync(request.CompanyId, reference, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Cb.ReferenceTaken", $"Reference {reference} is already used.");
        }

        if (!await _db.Customers.AnyAsync(c => c.Id == request.CustomerId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Cb.CustomerNotFound", $"Customer {request.CustomerId} not found.");
        }

        DirectDebitMandate mandate = new(request.CompanyId, reference, request.CustomerId, request.CustomerBankAccount.Trim(), request.BankAccountCode.Trim(), request.Frequency, request.StartDate,
            request.EndDate, request.FixedAmount, request.MaximumAmount);
        _db.Add(mandate);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return mandate.Id;
    }

    /// <inheritdoc />
    public async Task<Result> CancelAsync(long companyId, string reference, CancellationToken ct = default)
    {
        string key = (reference ?? string.Empty).Trim().ToUpperInvariant();
        StandingOrder? order = await _db.Set<StandingOrder>().FirstOrDefaultAsync(o => o.CompanyId == companyId && o.Reference == key, ct).ConfigureAwait(false);
        DirectDebitMandate? mandate = order is null ? await _db.Set<DirectDebitMandate>().FirstOrDefaultAsync(m => m.CompanyId == companyId && m.Reference == key, ct).ConfigureAwait(false) : null;
        if (order is null && mandate is null)
        {
            return Result.Failure(Error.NotFound("Cb.ReferenceNotFound", $"No standing order or mandate {key}."));
        }

        if (order is not null)
        {
            order.IsActive = false;
        }

        if (mandate is not null)
        {
            mandate.IsActive = false;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<RecurringBankRun>> RunDueAsync(long companyId, DateOnly asOf, CancellationToken ct = default)
    {
        List<RecurringBankLine> lines = [];
        int nothingOwed = 0;
        foreach (long id in await _db.Set<StandingOrder>().Where(o => o.CompanyId == companyId && o.IsActive).OrderBy(o => o.Reference).Select(o => o.Id).ToListAsync(ct).ConfigureAwait(false))
        {
            StandingOrder order = await _db.Set<StandingOrder>().SingleAsync(o => o.Id == id, ct).ConfigureAwait(false);
            foreach (DateOnly due in Recurrence.Due(order.StartDate, order.EndDate, order.Frequency, order.LastRunOn, asOf))
            {
                Result<RecurringBankLine> paid = await _db.AtomicAsync(() => PayAsync(order, due, ct), ct).ConfigureAwait(false);
                if (paid.IsFailure)
                {
                    return paid.Error;
                }

                lines.Add(paid.Value);
            }
        }

        foreach (long id in await _db.Set<DirectDebitMandate>().Where(m => m.CompanyId == companyId && m.IsActive).OrderBy(m => m.Reference).Select(m => m.Id).ToListAsync(ct).ConfigureAwait(false))
        {
            DirectDebitMandate mandate = await _db.Set<DirectDebitMandate>().SingleAsync(m => m.Id == id, ct).ConfigureAwait(false);
            foreach (DateOnly due in Recurrence.Due(mandate.StartDate, mandate.EndDate, mandate.Frequency, mandate.LastRunOn, asOf))
            {
                Result<RecurringBankLine?> collected = await _db.AtomicAsync(() => CollectAsync(mandate, due, ct), ct).ConfigureAwait(false);
                if (collected.IsFailure)
                {
                    return collected.Error;
                }

                if (collected.Value is RecurringBankLine line)
                {
                    lines.Add(line);
                }
                else
                {
                    nothingOwed++;
                }
            }
        }

        return new RecurringBankRun(lines, nothingOwed);
    }

    // One standing order occurrence: a supplier payment or a GL journal against the bank, then the order moves on.
    private async Task<Result<RecurringBankLine>> PayAsync(StandingOrder order, DateOnly due, CancellationToken ct)
    {
        string document = $"{order.Reference}-{due:yyyyMMdd}";
        if (order.SupplierId is long supplierId)
        {
            CurrencyCode currency = await _db.Suppliers.Where(s => s.Id == supplierId).Select(s => s.Currency).SingleAsync(ct).ConfigureAwait(false);
            Result<TradingPostingResult> payment = await _payables.PostPaymentAsync(new PaymentRequest
            {
                CompanyId = order.CompanyId, SupplierId = supplierId, DocumentNumber = document, PaymentDate = due, Currency = currency, Amount = order.Amount,
                BankAccountCode = order.BankAccountCode, Narrative = order.Narrative,
            }, ct).ConfigureAwait(false);
            if (payment.IsFailure)
            {
                return payment.Error;
            }
        }
        else
        {
            CurrencyCode currency = await _db.Companies.Where(c => c.Id == order.CompanyId).Select(c => c.BaseCurrency).SingleAsync(ct).ConfigureAwait(false);
            Result<PostingReceipt> posted = await _engine.PostAsync(new JournalRequest
            {
                CompanyId = order.CompanyId, SourceModule = "GL", PostingDate = due, TransactionCurrency = currency, EnforceManualJournalRules = false,
                DocumentType = "StandingOrder", DocumentNumber = document, Narrative = order.Narrative,
                Lines =
                [
                    new JournalLineRequest { AccountCode = order.DebitAccountCode!, Side = JournalSide.Debit, TransactionAmount = order.Amount },
                    new JournalLineRequest { AccountCode = order.BankAccountCode, Side = JournalSide.Credit, TransactionAmount = order.Amount },
                ],
            }, ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }
        }

        order.LastRunOn = due;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new RecurringBankLine(order.Reference, due, document, order.Amount, IsCollection: false);
    }

    // One direct debit occurrence: the fixed amount, or what the customer owes on the date (capped), receipted to the sales ledger.
    private async Task<Result<RecurringBankLine?>> CollectAsync(DirectDebitMandate mandate, DateOnly due, CancellationToken ct)
    {
        Customer customer = await _db.Customers.SingleAsync(c => c.Id == mandate.CustomerId, ct).ConfigureAwait(false);
        decimal amount = mandate.FixedAmount ?? await _db.OpenItems
            .Where(o => o.CompanyId == mandate.CompanyId && o.Ledger == TradingLedger.AccountsReceivable && o.TraderId == mandate.CustomerId && o.DocumentDate <= due)
            .SumAsync(o => o.Sign == LedgerSign.Debit ? o.OutstandingTransaction : -o.OutstandingTransaction, ct).ConfigureAwait(false);
        if (mandate.MaximumAmount is decimal cap)
        {
            amount = Math.Min(amount, cap);
        }

        if (amount <= 0m)
        {
            mandate.LastRunOn = due;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<RecurringBankLine?>.Success(null);
        }

        string document = $"{mandate.Reference}-{due:yyyyMMdd}";
        Result<TradingPostingResult> receipt = await _receivables.PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = mandate.CompanyId, CustomerId = customer.Id, DocumentNumber = document, ReceiptDate = due, Currency = customer.Currency, Amount = amount,
            BankAccountCode = mandate.BankAccountCode, Narrative = $"Direct debit {mandate.Reference}",
        }, ct).ConfigureAwait(false);
        if (receipt.IsFailure)
        {
            return receipt.Error;
        }

        mandate.LastRunOn = due;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<RecurringBankLine?>.Success(new RecurringBankLine(mandate.Reference, due, document, amount, IsCollection: true));
    }

    private async Task<bool> ReferenceTakenAsync(long companyId, string reference, CancellationToken ct)
        => await _db.Set<StandingOrder>().AnyAsync(o => o.CompanyId == companyId && o.Reference == reference, ct).ConfigureAwait(false)
            || await _db.Set<DirectDebitMandate>().AnyAsync(m => m.CompanyId == companyId && m.Reference == reference, ct).ConfigureAwait(false);
}
