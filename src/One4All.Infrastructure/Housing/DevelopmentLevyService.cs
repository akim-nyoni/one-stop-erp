using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Housing;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Development levies: definitions and their once-per-event charging on the sales ledger.</summary>
public sealed class DevelopmentLevyService : IDevelopmentLevyService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ISalesLedgerService _ar;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="ar">The sales ledger.</param>
    public DevelopmentLevyService(One4AllDbContext db, INumberSequenceService numbers, ISalesLedgerService ar)
    {
        _db = db;
        _numbers = numbers;
        _ar = ar;
    }

    /// <inheritdoc />
    public async Task<Result<long>> DefineAsync(long companyId, string code, string name, LevyTrigger trigger, LevyBasis basis, decimal rate, string incomeAccountCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(incomeAccountCode)
            || !Enum.IsDefined(trigger) || !Enum.IsDefined(basis) || rate <= 0m || (basis == LevyBasis.PercentOfPrice && rate > 100m))
        {
            return Error.Validation("Hse.Levy", "A levy needs a code, name, trigger, basis, a positive rate (a percentage no more than 100) and its income account.");
        }

        string key = code.Trim().ToUpperInvariant();
        if (await _db.Set<DevelopmentLevy>().AnyAsync(l => l.CompanyId == companyId && l.Code == key, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.LevyExists", $"Levy {key} already exists.");
        }

        DevelopmentLevy levy = new(companyId, key, name.Trim(), trigger, basis, rate, incomeAccountCode.Trim());
        _db.Set<DevelopmentLevy>().Add(levy);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return levy.Id;
    }

    /// <inheritdoc />
    public async Task<Result> DeactivateAsync(long companyId, string code, CancellationToken ct = default)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        DevelopmentLevy? levy = await _db.Set<DevelopmentLevy>().FirstOrDefaultAsync(l => l.CompanyId == companyId && l.Code == key, ct).ConfigureAwait(false);
        if (levy is null)
        {
            return Result.Failure(Error.NotFound("Hse.LevyNotFound", $"Levy {key} not found."));
        }

        levy.Deactivate();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetDeferralAsync(long companyId, string code, int months, string? deferredIncomeAccountCode, CancellationToken ct = default)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        DevelopmentLevy? levy = await _db.Set<DevelopmentLevy>().FirstOrDefaultAsync(l => l.CompanyId == companyId && l.Code == key, ct).ConfigureAwait(false);
        if (levy is null)
        {
            return Result.Failure(Error.NotFound("Hse.LevyNotFound", $"No levy {key}."));
        }

        if (months < 0 || (months > 0 && string.IsNullOrWhiteSpace(deferredIncomeAccountCode)))
        {
            return Result.Failure(Error.Validation("Hse.LevyDeferral", "A deferred levy needs a positive number of months and the deferred income account."));
        }

        levy.Defer(months, deferredIncomeAccountCode?.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<LevyRaised>>> ChargeAllocationAsync(long companyId, string certificateNumber, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => ChargeAllocationCoreAsync(companyId, certificateNumber, date, ct), ct);

    private async Task<Result<IReadOnlyList<LevyRaised>>> ChargeAllocationCoreAsync(long companyId, string certificateNumber, DateOnly date, CancellationToken ct)
    {
        string number = (certificateNumber ?? string.Empty).Trim();
        StandAllocation? allocation = await _db.Set<StandAllocation>().AsNoTracking().FirstOrDefaultAsync(a => a.CompanyId == companyId && a.CertificateNumber == number, ct).ConfigureAwait(false);
        if (allocation is null)
        {
            return Error.NotFound("Hse.CertificateNotFound", $"Allocation certificate {number} not found.");
        }

        if (allocation.Status != AllocationStatus.Accepted)
        {
            return Error.Rule("Hse.NotAccepted", $"The allocation under {number} is {allocation.Status}; levies are raised once it is accepted.");
        }

        string idNumber = await _db.HousingApplicants.Where(a => a.Id == allocation.ApplicantId).Select(a => a.IdNumber).FirstAsync(ct).ConfigureAwait(false);
        Customer? beneficiary = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CompanyId == companyId && c.NationalIdNumber == idNumber, ct).ConfigureAwait(false);
        if (beneficiary is null)
        {
            return Error.Rule("Hse.NoCustomerAccount", $"The beneficiary (ID {idNumber}) has no customer account to charge; open one first.");
        }

        return await ChargeAsync(companyId, LevyTrigger.Allocation, allocation.StandId, beneficiary, number, allocation.OfferedPrice, date, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<LevyRaised>>> ChargeTransferAsync(long companyId, string applicationNumber, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => ChargeTransferCoreAsync(companyId, applicationNumber, date, ct), ct);

    private async Task<Result<IReadOnlyList<LevyRaised>>> ChargeTransferCoreAsync(long companyId, string applicationNumber, DateOnly date, CancellationToken ct)
    {
        string number = (applicationNumber ?? string.Empty).Trim();
        TransferApplication? transfer = await _db.Set<TransferApplication>().AsNoTracking().FirstOrDefaultAsync(a => a.CompanyId == companyId && a.ApplicationNumber == number, ct).ConfigureAwait(false);
        if (transfer is null)
        {
            return Error.NotFound("Hse.TransferNotFound", $"Transfer application {number} not found.");
        }

        if (transfer.Status == TransferStatus.Rejected)
        {
            return Error.Rule("Hse.TransferClosed", $"{number} was rejected; no levies are raised.");
        }

        Customer incoming = await _db.Customers.AsNoTracking().FirstAsync(c => c.Id == transfer.ToCustomerId, ct).ConfigureAwait(false);
        return await ChargeAsync(companyId, LevyTrigger.Transfer, transfer.StandId, incoming, number, transfer.Consideration, date, ct).ConfigureAwait(false);
    }

    // Raises every active levy for the trigger not yet raised on this event, one invoice each.
    private async Task<Result<IReadOnlyList<LevyRaised>>> ChargeAsync(long companyId, LevyTrigger trigger, long standId, Customer customer, string eventReference, decimal? price, DateOnly date, CancellationToken ct)
    {
        decimal size = await _db.HousingStands.Where(s => s.Id == standId).Select(s => s.SizeSquareMetres).FirstAsync(ct).ConfigureAwait(false);
        List<DevelopmentLevy> levies = await _db.Set<DevelopmentLevy>().AsNoTracking()
            .Where(l => l.CompanyId == companyId && l.Trigger == trigger && l.IsActive).OrderBy(l => l.Code).ToListAsync(ct).ConfigureAwait(false);
        List<long> levyIds = [.. levies.Select(l => l.Id)];
        HashSet<long> raised = [.. await _db.Set<LevyCharge>().Where(c => levyIds.Contains(c.LevyId) && c.EventReference == eventReference).Select(c => c.LevyId).ToListAsync(ct).ConfigureAwait(false)];

        List<LevyRaised> charged = [];
        foreach (DevelopmentLevy levy in levies.Where(l => !raised.Contains(l.Id)))
        {
            decimal? amount = LevyCalculator.Amount(levy.Basis, levy.Rate, size, price);
            if (amount is null)
            {
                return Error.Rule("Hse.LevyNoPrice", $"Levy {levy.Code} is a percentage of the price, but {eventReference} carries no price.");
            }

            if (amount <= 0m)
            {
                continue;
            }

            Result<string> document = await _numbers.AllocateAsync(companyId, "LEV", date, ct: ct).ConfigureAwait(false);
            if (document.IsFailure)
            {
                return document.Error;
            }

            // A deferred levy is invoiced to its deferred income account and released to income month by month.
            bool deferred = levy.DeferMonths is > 0 && levy.DeferredIncomeAccountCode is not null;
            Result<TradingPostingResult> invoiced = await _ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = document.Value, DocumentDate = date, Currency = customer.Currency,
                NetAmount = amount.Value, SalesAccountCode = deferred ? levy.DeferredIncomeAccountCode! : levy.IncomeAccountCode, Narrative = $"{levy.Name} ({eventReference})",
            }, ct).ConfigureAwait(false);
            if (invoiced.IsFailure)
            {
                return invoiced.Error;
            }

            if (deferred)
            {
                _db.Set<PrepaymentSchedule>().Add(new PrepaymentSchedule(companyId, document.Value, $"{levy.Name} ({eventReference}) recognised", levy.DeferredIncomeAccountCode!,
                    levy.IncomeAccountCode, amount.Value, new DateOnly(date.Year, date.Month, 1), levy.DeferMonths!.Value) { IsDeferredIncome = true });
            }

            _db.Set<LevyCharge>().Add(new LevyCharge(companyId, levy.Id, standId, customer.Id, eventReference, amount.Value, document.Value, date));
            charged.Add(new LevyRaised(levy.Code, amount.Value, document.Value));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<IReadOnlyList<LevyRaised>>.Success(charged);
    }
}
