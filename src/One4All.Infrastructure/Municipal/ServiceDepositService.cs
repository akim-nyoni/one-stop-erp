using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>Service deposits held as a liability, refunded or applied to arrears through the ledger.</summary>
public sealed class ServiceDepositService : IServiceDepositService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly IPostingEngine _engine;
    private readonly ISalesLedgerService _ar;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="engine">The posting engine.</param>
    /// <param name="ar">The receivables ledger.</param>
    public ServiceDepositService(One4AllDbContext db, INumberSequenceService numbers, IPostingEngine engine, ISalesLedgerService ar)
    {
        _db = db;
        _numbers = numbers;
        _engine = engine;
        _ar = ar;
    }

    /// <inheritdoc />
    public Task<Result<DepositTaken>> TakeAsync(TakeDepositRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _db.AtomicAsync(() => TakeCoreAsync(request, ct), ct);
    }

    private async Task<Result<DepositTaken>> TakeCoreAsync(TakeDepositRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0m || string.IsNullOrWhiteSpace(request.BankAccountCode) || string.IsNullOrWhiteSpace(request.LiabilityAccountCode))
        {
            return Error.Validation("Mun.DepositRequest", "A deposit needs a positive amount, a bank account and a liability account.");
        }

        Property? property = await _db.Properties.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PropertyId && p.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (property is null)
        {
            return Error.NotFound("Mun.PropertyNotFound", $"Property {request.PropertyId} not found.");
        }

        if (await _db.Set<ServiceDeposit>().AnyAsync(d => d.CompanyId == request.CompanyId && d.PropertyId == request.PropertyId && d.ServiceType == request.ServiceType && d.Status == ServiceDepositStatus.Held, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Mun.DepositHeld", $"Stand {property.StandNumber} already holds a {request.ServiceType} deposit.");
        }

        Result<string> number = await _numbers.AllocateAsync(request.CompanyId, "DEP", request.Date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        CurrencyCode currency = await BaseCurrencyAsync(request.CompanyId, ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _engine.PostAsync(new JournalRequest
        {
            CompanyId = request.CompanyId, SourceModule = "CB", PostingDate = request.Date, TransactionCurrency = currency, DocumentType = "ServiceDeposit",
            DocumentNumber = number.Value, Narrative = $"{request.ServiceType} deposit, stand {property.StandNumber}",
            Lines =
            [
                new JournalLineRequest { AccountCode = request.BankAccountCode.Trim(), Side = JournalSide.Debit, TransactionAmount = request.Amount },
                new JournalLineRequest { AccountCode = request.LiabilityAccountCode.Trim(), Side = JournalSide.Credit, TransactionAmount = request.Amount },
            ],
        }, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        _db.Set<ServiceDeposit>().Add(new ServiceDeposit(request.CompanyId, number.Value, property.Id, property.RatepayerCustomerId, request.ServiceType, request.Amount, request.LiabilityAccountCode.Trim(), request.Date));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new DepositTaken(number.Value, request.Amount, posted.Value.JournalNumber);
    }

    /// <inheritdoc />
    public Task<Result<string>> RefundAsync(long companyId, string reference, DateOnly date, string bankAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => RefundCoreAsync(companyId, reference, date, bankAccountCode, ct), ct);

    private async Task<Result<string>> RefundCoreAsync(long companyId, string reference, DateOnly date, string bankAccountCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bankAccountCode))
        {
            return Error.Validation("Mun.DepositBank", "A refund needs a bank account.");
        }

        Result<ServiceDeposit> found = await HeldDepositAsync(companyId, reference, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        ServiceDeposit deposit = found.Value;
        CurrencyCode currency = await BaseCurrencyAsync(companyId, ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "CB", PostingDate = date, TransactionCurrency = currency, DocumentType = "ServiceDepositRefund",
            DocumentNumber = $"{deposit.Reference}-R", Narrative = $"Refund of {deposit.ServiceType} deposit {deposit.Reference}",
            Lines =
            [
                new JournalLineRequest { AccountCode = deposit.LiabilityAccountCode, Side = JournalSide.Debit, TransactionAmount = deposit.Amount },
                new JournalLineRequest { AccountCode = bankAccountCode.Trim(), Side = JournalSide.Credit, TransactionAmount = deposit.Amount },
            ],
        }, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        deposit.Release(ServiceDepositStatus.Refunded, date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return posted.Value.JournalNumber;
    }

    /// <inheritdoc />
    public Task<Result<string>> ApplyToArrearsAsync(long companyId, string reference, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => ApplyCoreAsync(companyId, reference, date, ct), ct);

    private async Task<Result<string>> ApplyCoreAsync(long companyId, string reference, DateOnly date, CancellationToken ct)
    {
        Result<ServiceDeposit> found = await HeldDepositAsync(companyId, reference, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        ServiceDeposit deposit = found.Value;
        CurrencyCode currency = await BaseCurrencyAsync(companyId, ct).ConfigureAwait(false);
        string document = $"{deposit.Reference}-F";

        // Dr the deposit liability / Cr the AR control: a receipt on the ratepayer's account funded from the deposit.
        Result<TradingPostingResult> receipt = await _ar.PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = deposit.RatepayerCustomerId, DocumentNumber = document, ReceiptDate = date, Currency = currency,
            Amount = deposit.Amount, BankAccountCode = deposit.LiabilityAccountCode, Narrative = $"{deposit.ServiceType} deposit {deposit.Reference} applied to account",
        }, ct).ConfigureAwait(false);
        if (receipt.IsFailure)
        {
            return receipt.Error;
        }

        deposit.Release(ServiceDepositStatus.Forfeited, date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return document;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HeldDeposit>> HeldAsync(long companyId, long propertyId, CancellationToken ct = default)
        => await _db.Set<ServiceDeposit>().AsNoTracking()
            .Where(d => d.CompanyId == companyId && d.PropertyId == propertyId && d.Status == ServiceDepositStatus.Held)
            .OrderBy(d => d.ServiceType)
            .Select(d => new HeldDeposit(d.Reference, d.ServiceType, d.Amount, d.TakenOn))
            .ToListAsync(ct).ConfigureAwait(false);

    private async Task<Result<ServiceDeposit>> HeldDepositAsync(long companyId, string reference, CancellationToken ct)
    {
        string number = (reference ?? string.Empty).Trim();
        ServiceDeposit? deposit = await _db.Set<ServiceDeposit>().FirstOrDefaultAsync(d => d.CompanyId == companyId && d.Reference == number, ct).ConfigureAwait(false);
        if (deposit is null)
        {
            return Error.NotFound("Mun.DepositNotFound", $"Deposit {number} not found.");
        }

        if (deposit.Status != ServiceDepositStatus.Held)
        {
            return Error.Rule("Mun.DepositReleased", $"Deposit {number} is {deposit.Status} and can no longer be released.");
        }

        return deposit;
    }

    private Task<CurrencyCode> BaseCurrencyAsync(long companyId, CancellationToken ct)
        => _db.Set<Company>().Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct);
}
