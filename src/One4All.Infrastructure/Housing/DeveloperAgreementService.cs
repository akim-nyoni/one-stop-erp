using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Developer / partner agreements: quotas, milestones, hand-overs, reconciliation and late-delivery penalties.</summary>
public sealed class DeveloperAgreementService : IDeveloperAgreementService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ISalesLedgerService _ar;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="ar">The sales ledger.</param>
    public DeveloperAgreementService(One4AllDbContext db, INumberSequenceService numbers, ISalesLedgerService ar)
    {
        _db = db;
        _numbers = numbers;
        _ar = ar;
    }

    /// <inheritdoc />
    public async Task<Result<string>> CreateAsync(DeveloperAgreementTerms terms, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (terms.CouncilQuota < 0 || terms.DeveloperQuota < 0 || terms.CouncilQuota + terms.DeveloperQuota == 0
            || terms.PenaltyPerStandPerMonth < 0m || terms.DeliveryDeadline <= terms.SignedOn)
        {
            return Error.Validation("Hse.DeveloperTerms", "An agreement needs stands in its quotas, no negative penalty, and a deadline after signature.");
        }

        if (!await _db.HousingSchemes.AnyAsync(s => s.Id == terms.SchemeId && s.CompanyId == terms.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Hse.SchemeNotFound", $"Scheme {terms.SchemeId} not found.");
        }

        if (!await _db.Customers.AnyAsync(c => c.Id == terms.DeveloperCustomerId && c.CompanyId == terms.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Hse.DeveloperNotFound", $"Customer {terms.DeveloperCustomerId} not found.");
        }

        Result<string> number = await _numbers.AllocateAsync(terms.CompanyId, "DEV", terms.SignedOn, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        _db.Set<DeveloperAgreement>().Add(new DeveloperAgreement(terms.CompanyId, number.Value, terms.SchemeId, terms.DeveloperCustomerId, terms.CouncilQuota,
            terms.DeveloperQuota, terms.SignedOn, terms.DeliveryDeadline, terms.PenaltyPerStandPerMonth));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public async Task<Result> AddMilestoneAsync(long companyId, string agreementNumber, string description, DateOnly dueOn, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return Result.Failure(Error.Validation("Hse.Milestone", "Describe the milestone."));
        }

        Result<DeveloperAgreement> found = await FindAsync(companyId, agreementNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        string text = description.Trim();
        if (found.Value.Milestones.Any(m => string.Equals(m.Description, text, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure(Error.Conflict("Hse.MilestoneExists", $"{found.Value.AgreementNumber} already has the milestone \"{text}\"."));
        }

        found.Value.AddMilestone(new DeliveryMilestone(text, dueOn));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> CompleteMilestoneAsync(long companyId, string agreementNumber, string description, DateOnly date, CancellationToken ct = default)
    {
        Result<DeveloperAgreement> found = await FindAsync(companyId, agreementNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        DeliveryMilestone? milestone = found.Value.Milestones.FirstOrDefault(m => string.Equals(m.Description, (description ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
        if (milestone is null)
        {
            return Result.Failure(Error.NotFound("Hse.MilestoneNotFound", $"{found.Value.AgreementNumber} has no milestone \"{description}\"."));
        }

        if (milestone.CompletedOn is not null)
        {
            return Result.Failure(Error.Conflict("Hse.MilestoneDone", $"\"{milestone.Description}\" was achieved on {milestone.CompletedOn:yyyy-MM-dd}."));
        }

        milestone.Complete(date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<int>> AssignStandsAsync(long companyId, string agreementNumber, StandQuota quota, IReadOnlyList<long> standIds, DateOnly date, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(standIds);
        return _db.AtomicAsync(() => AssignCoreAsync(companyId, agreementNumber, quota, standIds, date, ct), ct);
    }

    private async Task<Result<int>> AssignCoreAsync(long companyId, string agreementNumber, StandQuota quota, IReadOnlyList<long> standIds, DateOnly date, CancellationToken ct)
    {
        if (standIds.Count == 0 || standIds.Distinct().Count() != standIds.Count || !Enum.IsDefined(quota))
        {
            return Error.Validation("Hse.DeveloperStands", "Name a quota and list each stand once.");
        }

        Result<DeveloperAgreement> found = await FindAsync(companyId, agreementNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        DeveloperAgreement agreement = found.Value;
        int contracted = quota == StandQuota.Council ? agreement.CouncilQuota : agreement.DeveloperQuota;
        if (agreement.Stands.Count(s => s.Quota == quota) + standIds.Count > contracted)
        {
            return Error.Rule("Hse.QuotaExceeded", $"The {quota.ToString().ToLowerInvariant()} quota of {agreement.AgreementNumber} is {contracted} stands.");
        }

        List<HousingStand> stands = await _db.HousingStands.Where(s => s.CompanyId == companyId && standIds.Contains(s.Id)).ToListAsync(ct).ConfigureAwait(false);
        if (stands.Count != standIds.Count || stands.Any(s => s.SchemeId != agreement.SchemeId))
        {
            return Error.NotFound("Hse.StandNotInScheme", "Every stand must be on the register in the agreement's scheme.");
        }

        if (stands.Any(s => s.Status != StandStatus.Available) || await StandClaims.AnyClaimedAsync(_db, standIds, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.StandUnavailable", "Every stand must be available and not already allocated.");
        }

        foreach (HousingStand stand in stands)
        {
            stand.MarkAllocated(); // blocked while the developer services and builds
            agreement.AddStand(new DeveloperAgreementStand(stand.Id, quota, date));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return stands.Count;
    }

    /// <inheritdoc />
    public Task<Result> HandOverAsync(long companyId, string agreementNumber, long standId, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => HandOverCoreAsync(companyId, agreementNumber, standId, date, ct), ct);

    private async Task<Result> HandOverCoreAsync(long companyId, string agreementNumber, long standId, DateOnly date, CancellationToken ct)
    {
        Result<DeveloperAgreement> found = await FindAsync(companyId, agreementNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        DeveloperAgreementStand? assigned = found.Value.Stands.FirstOrDefault(s => s.StandId == standId);
        if (assigned is null)
        {
            return Result.Failure(Error.Rule("Hse.NotAgreementStand", $"The stand is not on {found.Value.AgreementNumber}."));
        }

        if (assigned.HandedOverOn is not null)
        {
            return Result.Failure(Error.Conflict("Hse.AlreadyHandedOver", $"The stand was handed over on {assigned.HandedOverOn:yyyy-MM-dd}."));
        }

        if (date < assigned.AssignedOn)
        {
            return Result.Failure(Error.Validation("Hse.HandOverDate", "A stand cannot be handed over before it was assigned."));
        }

        assigned.HandOver(date);
        if (assigned.Quota == StandQuota.Council)
        {
            HousingStand stand = await _db.HousingStands.FirstAsync(s => s.Id == standId, ct).ConfigureAwait(false);
            stand.ReleaseAllocation(); // back in the council's pool, ready for its own allocation
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<DeveloperReconciliation>> ReconcileAsync(long companyId, string agreementNumber, DateOnly asAt, CancellationToken ct = default)
    {
        Result<DeveloperAgreement> found = await FindAsync(companyId, agreementNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        return Reconcile(found.Value, asAt);
    }

    /// <inheritdoc />
    public Task<Result<decimal>> ChargePenaltiesAsync(long companyId, string agreementNumber, DateOnly asAt, string penaltyIncomeAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => ChargeCoreAsync(companyId, agreementNumber, asAt, penaltyIncomeAccountCode, ct), ct);

    private async Task<Result<decimal>> ChargeCoreAsync(long companyId, string agreementNumber, DateOnly asAt, string penaltyIncomeAccountCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(penaltyIncomeAccountCode))
        {
            return Error.Validation("Hse.PenaltyAccount", "Name the penalty income account.");
        }

        Result<DeveloperAgreement> found = await FindAsync(companyId, agreementNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        DeveloperAgreement agreement = found.Value;
        decimal due = Reconcile(agreement, asAt).PenaltyAccrued - agreement.PenaltiesCharged;
        if (due <= 0m)
        {
            return 0m;
        }

        Result<string> number = await _numbers.AllocateAsync(companyId, "DVP", asAt, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        Customer developer = await _db.Customers.AsNoTracking().FirstAsync(c => c.Id == agreement.DeveloperCustomerId, ct).ConfigureAwait(false);
        Result<TradingPostingResult> invoiced = await _ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = developer.Id, DocumentNumber = number.Value, DocumentDate = asAt, Currency = developer.Currency,
            NetAmount = due, SalesAccountCode = penaltyIncomeAccountCode.Trim(),
            Narrative = $"Late delivery penalty, {agreement.AgreementNumber}, to {asAt:yyyy-MM-dd}",
        }, ct).ConfigureAwait(false);
        if (invoiced.IsFailure)
        {
            return invoiced.Error;
        }

        agreement.ChargePenalty(due);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return due;
    }

    private static DeveloperReconciliation Reconcile(DeveloperAgreement agreement, DateOnly asAt)
    {
        List<QuotaDelivery> quotas = [];
        foreach ((StandQuota quota, int contracted) in new[] { (StandQuota.Council, agreement.CouncilQuota), (StandQuota.Developer, agreement.DeveloperQuota) })
        {
            List<DeveloperAgreementStand> stands = [.. agreement.Stands.Where(s => s.Quota == quota && s.AssignedOn <= asAt)];
            int handedOver = stands.Count(s => s.HandedOverOn <= asAt);
            quotas.Add(new QuotaDelivery(quota, contracted, stands.Count, handedOver, contracted - handedOver));
        }

        List<DateOnly> councilHandOvers = [.. agreement.Stands.Where(s => s.Quota == StandQuota.Council && s.HandedOverOn is not null).Select(s => s.HandedOverOn!.Value)];
        decimal penalty = DeveloperDelivery.Penalty(agreement.CouncilQuota, councilHandOvers, agreement.DeliveryDeadline, asAt, agreement.PenaltyPerStandPerMonth);
        List<string> overdue = [.. agreement.Milestones.Where(m => m.DueOn < asAt && (m.CompletedOn is null || m.CompletedOn > asAt)).OrderBy(m => m.DueOn).Select(m => m.Description)];
        return new DeveloperReconciliation(agreement.AgreementNumber, quotas, overdue, penalty, agreement.PenaltiesCharged);
    }

    private async Task<Result<DeveloperAgreement>> FindAsync(long companyId, string agreementNumber, CancellationToken ct)
    {
        string number = (agreementNumber ?? string.Empty).Trim();
        DeveloperAgreement? agreement = await _db.Set<DeveloperAgreement>().Include(a => a.Stands).Include(a => a.Milestones)
            .FirstOrDefaultAsync(a => a.CompanyId == companyId && a.AgreementNumber == number, ct).ConfigureAwait(false);
        if (agreement is null)
        {
            return Error.NotFound("Hse.DeveloperAgreementNotFound", $"Developer agreement {number} not found.");
        }

        return agreement;
    }
}
