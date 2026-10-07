using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Municipal;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>The housing waiting list: policy, registration checks, ranking and enquiry.</summary>
public sealed class WaitingListService : IWaitingListService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    public WaitingListService(One4AllDbContext db, INumberSequenceService numbers)
    {
        _db = db;
        _numbers = numbers;
    }

    /// <inheritdoc />
    public async Task<Result> SetPolicyAsync(long companyId, WaitingListPolicyInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        WaitingListTerms t = input.Terms;
        if (t.MinimumAge < 0 || t.MinimumResidencyYears < 0 || t.MaximumMonthlyIncome < 0m || t.PointsPerYearOnList < 0m || t.PointsPerYearResident < 0m
            || t.PointsPerDependant < 0m || t.DisabilityPoints < 0m || t.HomelessPoints < 0m || t.LodgerPoints < 0m)
        {
            return Result.Failure(Error.Validation("Hse.Policy", "Thresholds and weights cannot be negative."));
        }

        WaitingListPolicy? policy = await _db.Set<WaitingListPolicy>().FirstOrDefaultAsync(p => p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (policy is null)
        {
            policy = new WaitingListPolicy(companyId);
            _db.Set<WaitingListPolicy>().Add(policy);
        }

        policy.MinimumAge = t.MinimumAge;
        policy.MinimumResidencyYears = t.MinimumResidencyYears;
        policy.MaximumMonthlyIncome = t.MaximumMonthlyIncome;
        policy.PointsPerYearOnList = t.PointsPerYearOnList;
        policy.PointsPerYearResident = t.PointsPerYearResident;
        policy.PointsPerDependant = t.PointsPerDependant;
        policy.DisabilityPoints = t.DisabilityPoints;
        policy.HomelessPoints = t.HomelessPoints;
        policy.LodgerPoints = t.LodgerPoints;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<WaitingListRegistration>> RegisterAsync(WaitingListApplication application, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        return _db.AtomicAsync(() => RegisterCoreAsync(application, ct), ct);
    }

    private async Task<Result<WaitingListRegistration>> RegisterCoreAsync(WaitingListApplication application, CancellationToken ct)
    {
        string idNumber = NormaliseId(application.IdNumber);
        if (idNumber.Length == 0 || string.IsNullOrWhiteSpace(application.FullName) || application.Dependants < 0 || application.MonthlyIncome < 0m)
        {
            return Error.Validation("Hse.Application", "An application needs a national ID, a name, and no negative dependants or income.");
        }

        HousingApplicant? existing = await _db.HousingApplicants.FirstOrDefaultAsync(a => a.CompanyId == application.CompanyId && a.IdNumber == idNumber, ct).ConfigureAwait(false);
        if (existing is not null && existing.WaitingListNumber is not null && existing.Status is ApplicantStatus.Active or ApplicantStatus.Suspended)
        {
            return Error.Conflict("Hse.DuplicateApplicant", $"ID {idNumber} is already on the list as {existing.WaitingListNumber}.");
        }

        // One household, one stand: the spouse is checked like the applicant, and neither may already be on the list through the other.
        string? spouseId = string.IsNullOrWhiteSpace(application.SpouseIdNumber) ? null : NormaliseId(application.SpouseIdNumber);
        if (spouseId == idNumber)
        {
            return Error.Validation("Hse.SpouseSelf", "The spouse's ID is the applicant's own.");
        }

        if (spouseId is not null && application.MaritalStatus is not (MaritalStatus.Married or MaritalStatus.Cohabiting))
        {
            return Error.Validation("Hse.SpouseStatus", "A spouse is recorded only for a married or cohabiting applicant.");
        }

        ApplicantStatus[] onList = [ApplicantStatus.Active, ApplicantStatus.Suspended, ApplicantStatus.Allocated];
        string? household = await _db.HousingApplicants.AsNoTracking()
            .Where(a => a.CompanyId == application.CompanyId && a.IdNumber != idNumber && a.WaitingListNumber != null && onList.Contains(a.Status)
                && ((spouseId != null && (a.IdNumber == spouseId || a.SpouseIdNumber == spouseId)) || a.SpouseIdNumber == idNumber))
            .Select(a => a.WaitingListNumber).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (household is not null)
        {
            return Error.Conflict("Hse.SpouseOnList", $"This household is already on the list or allocated as {household} (through the spouse).");
        }

        WaitingListTerms terms = await TermsAsync(application.CompanyId, ct).ConfigureAwait(false);
        ApplicantFacts facts = new(application.DateOfBirth, application.ResidentSince, application.RegistrationDate, application.Dependants, application.HasDisability, application.MonthlyIncome, application.Tenure);
        bool ownsProperty = await OwnsPropertyAsync(application.CompanyId, idNumber, ct).ConfigureAwait(false)
            || (spouseId is not null && await OwnsPropertyAsync(application.CompanyId, spouseId, ct).ConfigureAwait(false));
        bool previouslyAllocated = existing?.Status == ApplicantStatus.Allocated;
        IReadOnlyList<string> problems = WaitingListRules.Problems(facts, terms, ownsProperty, previouslyAllocated, application.RegistrationDate);
        if (problems.Count > 0)
        {
            return Error.Rule("Hse.NotQualified", string.Join(" ", problems));
        }

        Result<string> number = await _numbers.AllocateAsync(application.CompanyId, "WL", application.RegistrationDate, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        HousingApplicant applicant = existing ?? new HousingApplicant(application.CompanyId, idNumber, application.FullName.Trim(), application.RegistrationDate);
        if (existing is null)
        {
            _db.HousingApplicants.Add(applicant);
        }

        applicant.FullName = application.FullName.Trim();
        applicant.SetDetails(application.Category, application.DateOfBirth, application.ResidentSince, application.Dependants, application.HasDisability, application.MonthlyIncome, application.Tenure);
        applicant.SetSpouse(application.MaritalStatus, application.MaritalRegime, spouseId, application.SpouseName);
        applicant.Register(number.Value, application.RegistrationDate);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new WaitingListRegistration(number.Value, WaitingListRules.Score(facts, terms, application.RegistrationDate));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WaitingListPlace>> RankingAsync(long companyId, HousingCategory category, WaitingListOrder order, DateOnly asAtDate, CancellationToken ct = default)
    {
        WaitingListTerms terms = await TermsAsync(companyId, ct).ConfigureAwait(false);
        List<HousingApplicant> active = await _db.HousingApplicants.AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.Category == category && a.Status == ApplicantStatus.Active && a.WaitingListNumber != null)
            .ToListAsync(ct).ConfigureAwait(false);
        var scored = active.Select(a => (Applicant: a, Score: WaitingListRules.Score(Facts(a), terms, asAtDate).Total));
        var ordered = order == WaitingListOrder.Score
            ? scored.OrderByDescending(s => s.Score).ThenBy(s => s.Applicant.RegistrationDate).ThenBy(s => s.Applicant.WaitingListNumber, StringComparer.Ordinal)
            : scored.OrderBy(s => s.Applicant.RegistrationDate).ThenBy(s => s.Applicant.WaitingListNumber, StringComparer.Ordinal);
        return [.. ordered.Select((s, i) => new WaitingListPlace(i + 1, s.Applicant.WaitingListNumber!, s.Applicant.FullName, s.Applicant.RegistrationDate, s.Score))];
    }

    /// <inheritdoc />
    public async Task<Result<WaitingListEnquiry>> EnquireAsync(long companyId, string waitingListNumberOrId, DateOnly asAtDate, CancellationToken ct = default)
    {
        string key = NormaliseId(waitingListNumberOrId);
        HousingApplicant? applicant = await _db.HousingApplicants.AsNoTracking()
            .FirstOrDefaultAsync(a => a.CompanyId == companyId && a.WaitingListNumber != null && (a.WaitingListNumber == key || a.IdNumber == key), ct).ConfigureAwait(false);
        if (applicant is null)
        {
            return Error.NotFound("Hse.ApplicantNotFound", $"No waiting-list registration for {key}.");
        }

        WaitingListTerms terms = await TermsAsync(companyId, ct).ConfigureAwait(false);
        int? position = null;
        if (applicant.Status == ApplicantStatus.Active)
        {
            IReadOnlyList<WaitingListPlace> ranking = await RankingAsync(companyId, applicant.Category, WaitingListOrder.Seniority, asAtDate, ct).ConfigureAwait(false);
            position = ranking.FirstOrDefault(p => p.WaitingListNumber == applicant.WaitingListNumber)?.Position;
        }

        return new WaitingListEnquiry(applicant.WaitingListNumber!, applicant.Category, applicant.Status, position, WaitingListRules.Score(Facts(applicant), terms, asAtDate));
    }

    private static ApplicantFacts Facts(HousingApplicant a)
        => new(a.DateOfBirth ?? a.RegistrationDate, a.ResidentSince ?? a.RegistrationDate, a.RegistrationDate, a.Dependants, a.HasDisability, a.MonthlyIncome, a.Tenure);

    private static string NormaliseId(string? value) => (value ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();

    private async Task<WaitingListTerms> TermsAsync(long companyId, CancellationToken ct)
    {
        WaitingListPolicy policy = await _db.Set<WaitingListPolicy>().AsNoTracking().FirstOrDefaultAsync(p => p.CompanyId == companyId, ct).ConfigureAwait(false)
            ?? new WaitingListPolicy(companyId);
        return WaitingListTerms.From(policy);
    }

    // Whether someone with this national ID holds a council stand or is a current owner on the municipal register.
    private async Task<bool> OwnsPropertyAsync(long companyId, string idNumber, CancellationToken ct)
    {
        List<long> customerIds = await _db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId && c.NationalIdNumber == idNumber)
            .Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        if (customerIds.Count == 0)
        {
            return false;
        }

        bool holdsStand = await _db.Set<HousingStand>().AnyAsync(s => s.CompanyId == companyId && s.CurrentHolderCustomerId != null && customerIds.Contains(s.CurrentHolderCustomerId.Value), ct).ConfigureAwait(false);
        bool ownsProperty = await _db.Set<PropertyOwner>().AnyAsync(o => o.CompanyId == companyId && o.OwnedTo == null && customerIds.Contains(o.CustomerId), ct).ConfigureAwait(false);
        return holdsStand || ownsProperty;
    }
}
