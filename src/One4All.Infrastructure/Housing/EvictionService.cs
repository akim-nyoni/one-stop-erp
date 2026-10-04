using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>The eviction workflow for council tenancies, ending in the tenancy's close-out on eviction.</summary>
public sealed class EvictionService : IEvictionService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly IRentalHousingService _rentals;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="rentals">The rental service, which ends the tenancy on eviction.</param>
    public EvictionService(One4AllDbContext db, INumberSequenceService numbers, IRentalHousingService rentals)
    {
        _db = db;
        _numbers = numbers;
        _rentals = rentals;
    }

    /// <inheritdoc />
    public Task<Result<string>> OpenAsync(long companyId, string tenancyNumber, DateOnly date, int noticePeriodDays, long userId, CancellationToken ct = default)
        => _db.AtomicAsync(() => OpenCoreAsync(companyId, tenancyNumber, date, noticePeriodDays, userId, ct), ct);

    private async Task<Result<string>> OpenCoreAsync(long companyId, string tenancyNumber, DateOnly date, int noticePeriodDays, long userId, CancellationToken ct)
    {
        if (noticePeriodDays < 1)
        {
            return Error.Validation("Hse.NoticeDays", "The notice period must be at least a day.");
        }

        string number = (tenancyNumber ?? string.Empty).Trim();
        Tenancy? tenancy = await _db.Set<Tenancy>().AsNoTracking().FirstOrDefaultAsync(t => t.CompanyId == companyId && t.TenancyNumber == number, ct).ConfigureAwait(false);
        if (tenancy is null)
        {
            return Error.NotFound("Hse.TenancyNotFound", $"Tenancy {number} not found.");
        }

        if (tenancy.Status != TenancyStatus.Active)
        {
            return Error.Rule("Hse.TenancyEnded", $"Tenancy {number} has ended.");
        }

        if (await _db.Set<EvictionCase>().AnyAsync(c => c.TenancyId == tenancy.Id && c.Status == EvictionCaseStatus.Open, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.EvictionOpen", $"Tenancy {number} already has an open eviction case.");
        }

        var items = await _db.OpenItems.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == tenancy.TenantCustomerId && i.OutstandingBase != 0m)
            .Select(i => new { i.Sign, i.OutstandingBase }).ToListAsync(ct).ConfigureAwait(false);
        decimal arrears = items.Sum(i => i.Sign == LedgerSign.Debit ? i.OutstandingBase : -i.OutstandingBase);
        if (arrears <= 0m)
        {
            return Error.Rule("Hse.NoArrears", $"Tenancy {number} owes nothing.");
        }

        Result<string> caseNumber = await _numbers.AllocateAsync(companyId, "EV", date, ct: ct).ConfigureAwait(false);
        if (caseNumber.IsFailure)
        {
            return caseNumber.Error;
        }

        _db.Set<EvictionCase>().Add(new EvictionCase(companyId, caseNumber.Value, tenancy.Id, arrears, noticePeriodDays, date, userId));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return caseNumber.Value;
    }

    /// <inheritdoc />
    public Task<Result> AdvanceAsync(long companyId, string caseNumber, EvictionStage stage, DateOnly date, string? reference, string? refundBankAccountCode, long userId, CancellationToken ct = default)
        => _db.AtomicAsync(() => AdvanceCoreAsync(companyId, caseNumber, stage, date, reference, refundBankAccountCode, userId, ct), ct);

    private async Task<Result> AdvanceCoreAsync(long companyId, string caseNumber, EvictionStage stage, DateOnly date, string? reference, string? refundBankAccountCode, long userId, CancellationToken ct)
    {
        Result<EvictionCase> found = await FindOpenAsync(companyId, caseNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        EvictionCase evictionCase = found.Value;
        EvictionStep current = evictionCase.Current;
        DateOnly? notice = evictionCase.Steps.FirstOrDefault(s => s.Stage == EvictionStage.NoticeToVacate)?.Date;
        string? problem = EvictionWorkflow.Problem(current.Stage, current.Date, notice, evictionCase.NoticePeriodDays, stage, date, reference);
        if (problem is not null)
        {
            return Result.Failure(Error.Rule("Hse.EvictionStage", problem));
        }

        if (stage == EvictionStage.Evicted)
        {
            // Eviction ends the tenancy: final rent, the deposit applied and refunded, the unit returned as void.
            string tenancyNumber = await _db.Set<Tenancy>().Where(t => t.Id == evictionCase.TenancyId).Select(t => t.TenancyNumber).FirstAsync(ct).ConfigureAwait(false);
            Result<TenancyEnded> ended = await _rentals.EndTenancyAsync(companyId, tenancyNumber, date, refundBankAccountCode ?? string.Empty, ct).ConfigureAwait(false);
            if (ended.IsFailure)
            {
                return Result.Failure(ended.Error);
            }
        }

        evictionCase.Advance(new EvictionStep(stage, date, string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(), userId));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> WithdrawAsync(long companyId, string caseNumber, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Hse.WithdrawReason", "Record why the case is withdrawn."));
        }

        Result<EvictionCase> found = await FindOpenAsync(companyId, caseNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        found.Value.Withdraw(reason.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EvictionCaseEntry>> RegisterAsync(long companyId, bool openOnly, CancellationToken ct = default)
    {
        List<EvictionCase> cases = await _db.Set<EvictionCase>().AsNoTracking().Include(c => c.Steps)
            .Where(c => c.CompanyId == companyId && (!openOnly || c.Status == EvictionCaseStatus.Open))
            .OrderBy(c => c.CaseNumber).ToListAsync(ct).ConfigureAwait(false);
        List<long> tenancyIds = [.. cases.Select(c => c.TenancyId)];
        var tenancies = await (
            from t in _db.Set<Tenancy>().AsNoTracking()
            join u in _db.Set<RentalUnit>().AsNoTracking() on t.UnitId equals u.Id
            where tenancyIds.Contains(t.Id)
            select new { t.Id, t.TenancyNumber, u.Code }).ToListAsync(ct).ConfigureAwait(false);
        return [.. cases.Select(c =>
        {
            var tenancy = tenancies.First(t => t.Id == c.TenancyId);
            EvictionStep current = c.Current;
            return new EvictionCaseEntry(c.CaseNumber, tenancy.TenancyNumber, tenancy.Code, c.ArrearsAtOpening, current.Stage, current.Date, c.Status);
        })];
    }

    private async Task<Result<EvictionCase>> FindOpenAsync(long companyId, string caseNumber, CancellationToken ct)
    {
        string number = (caseNumber ?? string.Empty).Trim();
        EvictionCase? evictionCase = await _db.Set<EvictionCase>().Include(c => c.Steps)
            .FirstOrDefaultAsync(c => c.CompanyId == companyId && c.CaseNumber == number, ct).ConfigureAwait(false);
        if (evictionCase is null)
        {
            return Error.NotFound("Hse.EvictionNotFound", $"Eviction case {number} not found.");
        }

        if (evictionCase.Status != EvictionCaseStatus.Open)
        {
            return Error.Rule("Hse.EvictionClosed", $"Eviction case {number} is {evictionCase.Status}.");
        }

        return evictionCase;
    }
}
