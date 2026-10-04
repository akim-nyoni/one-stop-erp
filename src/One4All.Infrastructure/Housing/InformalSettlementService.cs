using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Municipal;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Informal settlements: the register, enumeration, and regularisation of eligible households.</summary>
public sealed class InformalSettlementService : IInformalSettlementService
{
    private static readonly AllocationStatus[] Live = [AllocationStatus.Proposed, AllocationStatus.Offered, AllocationStatus.Accepted];

    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly IWaitingListService _waitingList;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="waitingList">The waiting list.</param>
    public InformalSettlementService(One4AllDbContext db, INumberSequenceService numbers, IWaitingListService waitingList)
    {
        _db = db;
        _numbers = numbers;
        _waitingList = waitingList;
    }

    /// <inheritdoc />
    public async Task<Result<long>> RegisterAsync(SettlementRegistration registration, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (string.IsNullOrWhiteSpace(registration.Code) || string.IsNullOrWhiteSpace(registration.Name) || string.IsNullOrWhiteSpace(registration.Ward))
        {
            return Error.Validation("Hse.SettlementDetails", "A settlement needs a code, a name and a ward.");
        }

        string key = registration.Code.Trim().ToUpperInvariant();
        if (await _db.Set<InformalSettlement>().AnyAsync(s => s.CompanyId == registration.CompanyId && s.Code == key, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.SettlementExists", $"Settlement {key} already exists.");
        }

        if (registration.LandParcelId is long parcelId && !await _db.LandParcels.AnyAsync(p => p.Id == parcelId && p.CompanyId == registration.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Hse.ParcelNotFound", $"Parcel {parcelId} not found.");
        }

        InformalSettlement settlement = new(registration.CompanyId, key, registration.Name.Trim(), registration.Ward.Trim(), registration.LandParcelId, registration.CutOffDate);
        _db.Set<InformalSettlement>().Add(settlement);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return settlement.Id;
    }

    /// <inheritdoc />
    public async Task<Result> ApprovePlanAsync(long companyId, string settlementCode, string plan, DateOnly approvedOn, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plan))
        {
            return Result.Failure(Error.Validation("Hse.UpgradingPlan", "Describe the upgrading plan."));
        }

        Result<InformalSettlement> found = await FindAsync(companyId, settlementCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        found.Value.ApprovePlan(plan.Trim(), approvedOn);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> StartEnumerationAsync(long companyId, string settlementCode, string reference, DateOnly date, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return Error.Validation("Hse.ExerciseReference", "An enumeration exercise needs a reference.");
        }

        Result<InformalSettlement> found = await FindAsync(companyId, settlementCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        long settlementId = found.Value.Id;
        if (await _db.Set<EnumerationExercise>().AnyAsync(e => e.SettlementId == settlementId && e.ClosedOn == null, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.ExerciseOpen", $"{found.Value.Code} already has an enumeration exercise open.");
        }

        EnumerationExercise exercise = new(companyId, settlementId, reference.Trim(), date);
        _db.Set<EnumerationExercise>().Add(exercise);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return exercise.Id;
    }

    /// <inheritdoc />
    public Task<Result<EnumerationResult>> EnumerateAsync(HouseholdSurvey survey, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(survey);
        return _db.AtomicAsync(() => EnumerateCoreAsync(survey, ct), ct);
    }

    private async Task<Result<EnumerationResult>> EnumerateCoreAsync(HouseholdSurvey survey, CancellationToken ct)
    {
        string idNumber = NormaliseId(survey.HeadIdNumber);
        if (idNumber.Length == 0 || string.IsNullOrWhiteSpace(survey.HeadName) || survey.HouseholdSize < 1 || survey.MonthlyIncome < 0m
            || string.IsNullOrWhiteSpace(survey.StructureReference) || !Enum.IsDefined(survey.StructureType))
        {
            return Error.Validation("Hse.HouseholdDetails", "A household needs the head's ID and name, at least one member, no negative income, and its structure.");
        }

        Result<InformalSettlement> found = await FindAsync(survey.CompanyId, survey.SettlementCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        InformalSettlement settlement = found.Value;
        EnumerationExercise? exercise = await _db.Set<EnumerationExercise>()
            .FirstOrDefaultAsync(e => e.SettlementId == settlement.Id && e.ClosedOn == null, ct).ConfigureAwait(false);
        if (exercise is null || survey.Date < exercise.StartedOn)
        {
            return Error.Rule("Hse.NoOpenExercise", $"{settlement.Code} has no enumeration exercise open on {survey.Date:yyyy-MM-dd}.");
        }

        string? existing = await _db.Set<EnumeratedHousehold>().Where(h => h.CompanyId == survey.CompanyId && h.HeadIdNumber == idNumber)
            .Select(h => h.HouseholdNumber).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return Error.Conflict("Hse.AlreadyEnumerated", $"ID {idNumber} is already enumerated as household {existing}.");
        }

        Result<string> number = await _numbers.AllocateAsync(survey.CompanyId, "ENU", survey.Date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        int minimumAge = await _db.Set<WaitingListPolicy>().Where(p => p.CompanyId == survey.CompanyId).Select(p => (int?)p.MinimumAge)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? new WaitingListPolicy(survey.CompanyId).MinimumAge;
        bool holdsProperty = await HoldsCouncilPropertyAsync(survey.CompanyId, idNumber, ct).ConfigureAwait(false);
        EnumeratedHousehold household = new(survey.CompanyId, settlement.Id, exercise.Id, number.Value, idNumber, survey.HeadName.Trim(), survey.HeadDateOfBirth,
            survey.HouseholdSize, survey.MonthlyIncome, survey.OccupiedSince, survey.StructureType, survey.StructureReference.Trim(), survey.Date);
        household.Assess(RegularisationRules.Problems(new HouseholdFacts(survey.HeadDateOfBirth, survey.OccupiedSince, survey.Date, holdsProperty), settlement.CutOffDate, minimumAge));
        _db.Set<EnumeratedHousehold>().Add(household);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new EnumerationResult(household.HouseholdNumber, household.IsEligible, household.IneligibleReason);
    }

    /// <inheritdoc />
    public async Task<Result<int>> CloseEnumerationAsync(long companyId, string settlementCode, DateOnly date, CancellationToken ct = default)
    {
        Result<InformalSettlement> found = await FindAsync(companyId, settlementCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        long settlementId = found.Value.Id;
        EnumerationExercise? exercise = await _db.Set<EnumerationExercise>().FirstOrDefaultAsync(e => e.SettlementId == settlementId && e.ClosedOn == null, ct).ConfigureAwait(false);
        if (exercise is null)
        {
            return Error.Rule("Hse.NoOpenExercise", $"{found.Value.Code} has no enumeration exercise open.");
        }

        if (date < exercise.StartedOn)
        {
            return Error.Validation("Hse.ExerciseDates", "An exercise cannot close before it started.");
        }

        exercise.Close(date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await _db.Set<EnumeratedHousehold>().CountAsync(h => h.ExerciseId == exercise.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<string>> ConvertToWaitingListAsync(long companyId, string householdNumber, HousingCategory category, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => ConvertCoreAsync(companyId, householdNumber, category, date, ct), ct);

    private async Task<Result<string>> ConvertCoreAsync(long companyId, string householdNumber, HousingCategory category, DateOnly date, CancellationToken ct)
    {
        Result<EnumeratedHousehold> found = await PendingEligibleAsync(companyId, householdNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        EnumeratedHousehold household = found.Value;
        Result<WaitingListRegistration> registered = await _waitingList.RegisterAsync(new WaitingListApplication
        {
            CompanyId = companyId, IdNumber = household.HeadIdNumber, FullName = household.HeadName, Category = category, DateOfBirth = household.HeadDateOfBirth,
            ResidentSince = household.OccupiedSince, Dependants = household.HouseholdSize - 1, MonthlyIncome = household.MonthlyIncome,
            Tenure = CurrentTenure.InformalSettlement, RegistrationDate = date,
        }, ct).ConfigureAwait(false);
        if (registered.IsFailure)
        {
            return registered.Error;
        }

        household.RegisterOnWaitingList(registered.Value.WaitingListNumber, date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return registered.Value.WaitingListNumber;
    }

    /// <inheritdoc />
    public Task<Result> RegulariseAsync(long companyId, string householdNumber, long standId, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => RegulariseCoreAsync(companyId, householdNumber, standId, date, ct), ct);

    private async Task<Result> RegulariseCoreAsync(long companyId, string householdNumber, long standId, DateOnly date, CancellationToken ct)
    {
        Result<EnumeratedHousehold> found = await PendingEligibleAsync(companyId, householdNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        EnumeratedHousehold household = found.Value;
        InformalSettlement settlement = await _db.Set<InformalSettlement>().FirstAsync(s => s.Id == household.SettlementId, ct).ConfigureAwait(false);
        if (settlement.PlanApprovedOn is null || settlement.PlanApprovedOn > date)
        {
            return Result.Failure(Error.Rule("Hse.NoUpgradingPlan", $"{settlement.Code} has no approved upgrading plan; households cannot be regularised yet."));
        }

        HousingStand? stand = await _db.HousingStands.FirstOrDefaultAsync(s => s.Id == standId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (stand is null)
        {
            return Result.Failure(Error.NotFound("Hse.StandNotFound", $"Stand {standId} not found."));
        }

        // The stand: available, and not promised to anyone through any other route.
        bool allocated = await _db.Set<StandAllocation>().AnyAsync(a => a.StandId == standId && Live.Contains(a.Status), ct).ConfigureAwait(false)
            || await _db.Set<CooperativeStand>().AnyAsync(c => c.StandId == standId, ct).ConfigureAwait(false)
            || await _db.Set<EnumeratedHousehold>().AnyAsync(h => h.StandId == standId, ct).ConfigureAwait(false);
        if (stand.Status != StandStatus.Available || allocated)
        {
            return Result.Failure(Error.Conflict("Hse.StandUnavailable", $"Stand {stand.StandNumber} is not available."));
        }

        // The person: not already housed through the waiting list, a cooperative, or as a holder or owner.
        string idNumber = household.HeadIdNumber;
        HousingApplicant? applicant = await _db.HousingApplicants.FirstOrDefaultAsync(a => a.CompanyId == companyId && a.IdNumber == idNumber, ct).ConfigureAwait(false);
        bool viaWaitingList = applicant is not null && (applicant.Status == ApplicantStatus.Allocated
            || await _db.Set<StandAllocation>().AnyAsync(a => a.ApplicantId == applicant.Id && Live.Contains(a.Status), ct).ConfigureAwait(false));
        bool viaCooperative = await (
            from c in _db.Set<CooperativeStand>()
            join m in _db.Set<CooperativeMember>() on c.MemberId equals m.Id
            join co in _db.Set<Cooperative>() on m.CooperativeId equals co.Id
            where co.CompanyId == companyId && m.IdNumber == idNumber
            select c.Id).AnyAsync(ct).ConfigureAwait(false);
        if (viaWaitingList || viaCooperative || await HoldsCouncilPropertyAsync(companyId, idNumber, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Hse.AlreadyHoused", $"{household.HeadName} already holds or has been allocated a stand or council property."));
        }

        stand.MarkAllocated();
        household.Regularise(standId, date);
        applicant?.SetStatus(ApplicantStatus.Allocated); // off the waiting list: housed on regularisation
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<SettlementSummary>> SummaryAsync(long companyId, string settlementCode, CancellationToken ct = default)
    {
        Result<InformalSettlement> found = await FindAsync(companyId, settlementCode, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        long settlementId = found.Value.Id;
        var households = await _db.Set<EnumeratedHousehold>().AsNoTracking().Where(h => h.SettlementId == settlementId)
            .Select(h => new { h.IsEligible, h.Outcome }).ToListAsync(ct).ConfigureAwait(false);
        return new SettlementSummary(found.Value.Code, found.Value.Name, found.Value.PlanApprovedOn, households.Count,
            households.Count(h => h.IsEligible),
            households.Count(h => h.Outcome == RegularisationOutcome.WaitingList),
            households.Count(h => h.Outcome == RegularisationOutcome.Regularised),
            households.Count(h => h.IsEligible && h.Outcome == RegularisationOutcome.Pending));
    }

    private static string NormaliseId(string? value) => (value ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();

    private async Task<Result<InformalSettlement>> FindAsync(long companyId, string settlementCode, CancellationToken ct)
    {
        string key = (settlementCode ?? string.Empty).Trim().ToUpperInvariant();
        InformalSettlement? settlement = await _db.Set<InformalSettlement>().FirstOrDefaultAsync(s => s.CompanyId == companyId && s.Code == key, ct).ConfigureAwait(false);
        if (settlement is null)
        {
            return Error.NotFound("Hse.SettlementNotFound", $"Settlement {key} not found.");
        }

        return settlement;
    }

    private async Task<Result<EnumeratedHousehold>> PendingEligibleAsync(long companyId, string householdNumber, CancellationToken ct)
    {
        string number = (householdNumber ?? string.Empty).Trim();
        EnumeratedHousehold? household = await _db.Set<EnumeratedHousehold>()
            .FirstOrDefaultAsync(h => h.CompanyId == companyId && h.HouseholdNumber == number, ct).ConfigureAwait(false);
        if (household is null)
        {
            return Error.NotFound("Hse.HouseholdNotFound", $"Household {number} not found.");
        }

        if (!household.IsEligible)
        {
            return Error.Rule("Hse.HouseholdIneligible", $"Household {number} does not qualify: {household.IneligibleReason}");
        }

        if (household.Outcome != RegularisationOutcome.Pending)
        {
            return Error.Conflict("Hse.HouseholdDealtWith", $"Household {number} has already been {(household.Outcome == RegularisationOutcome.WaitingList ? "registered on the waiting list" : "regularised")}.");
        }

        return household;
    }

    // Whether someone with this national ID holds a council stand or is a current owner on the municipal register.
    private async Task<bool> HoldsCouncilPropertyAsync(long companyId, string idNumber, CancellationToken ct)
    {
        List<long> customerIds = await _db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId && c.NationalIdNumber == idNumber)
            .Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        if (customerIds.Count == 0)
        {
            return false;
        }

        return await _db.HousingStands.AnyAsync(s => s.CompanyId == companyId && s.CurrentHolderCustomerId != null && customerIds.Contains(s.CurrentHolderCustomerId.Value), ct).ConfigureAwait(false)
            || await _db.Set<PropertyOwner>().AnyAsync(o => o.CompanyId == companyId && o.OwnedTo == null && customerIds.Contains(o.CustomerId), ct).ConfigureAwait(false);
    }
}
