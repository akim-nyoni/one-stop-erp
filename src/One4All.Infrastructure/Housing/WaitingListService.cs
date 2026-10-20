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
        policy.PointsPerYearOfAge = t.PointsPerYearOfAge;
        policy.AgePointsFromYears = Math.Max(0, t.AgePointsFromYears);
        policy.CooperativeMemberPoints = t.CooperativeMemberPoints;
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
        List<string> problems = [.. WaitingListRules.Problems(facts, terms, ownsProperty, previouslyAllocated, application.RegistrationDate)];
        QualificationRules rules = await QualificationRulesAsync(application.CompanyId, ct).ConfigureAwait(false);
        if (rules != QualificationRules.None)
        {
            List<string> householdIds = [idNumber, .. spouseId is null ? Array.Empty<string>() : [spouseId]];
            List<long> customers = await _db.Customers.AsNoTracking().Where(c => c.CompanyId == application.CompanyId && c.NationalIdNumber != null && householdIds.Contains(c.NationalIdNumber))
                .Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
            problems.AddRange(WaitingListRules.Problems(rules, new QualificationFacts(application.Category, application.MonthlyIncome,
                await EverHeldPropertyAsync(application.CompanyId, customers, ct).ConfigureAwait(false), await ForfeitedAsync(application.CompanyId, customers, ct).ConfigureAwait(false),
                await OverdueAsync(application.CompanyId, customers, application.RegistrationDate, ct).ConfigureAwait(false))));
        }

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
        HashSet<string> members = await CooperativeMembersAsync(companyId, ct).ConfigureAwait(false);
        var scored = active.Select(a => (Applicant: a, Score: WaitingListRules.Score(Facts(a, members), terms, asAtDate).Total));
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

        return new WaitingListEnquiry(applicant.WaitingListNumber!, applicant.Category, applicant.Status, position,
            WaitingListRules.Score(Facts(applicant, await CooperativeMembersAsync(companyId, ct).ConfigureAwait(false)), terms, asAtDate));
    }

    private static ApplicantFacts Facts(HousingApplicant a, IReadOnlySet<string> cooperativeMembers)
        => new(a.DateOfBirth ?? a.RegistrationDate, a.ResidentSince ?? a.RegistrationDate, a.RegistrationDate, a.Dependants, a.HasDisability, a.MonthlyIncome, a.Tenure,
            cooperativeMembers.Contains(NormaliseId(a.IdNumber)));

    // The ID numbers of everyone in the council's registered housing cooperatives.
    private async Task<HashSet<string>> CooperativeMembersAsync(long companyId, CancellationToken ct)
        => [.. (await _db.Set<Cooperative>().AsNoTracking().Where(c => c.CompanyId == companyId).SelectMany(c => c.Members).Select(m => m.IdNumber).ToListAsync(ct).ConfigureAwait(false))
            .Select(NormaliseId)];

    private static string NormaliseId(string? value) => (value ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();

    /// <inheritdoc />
    public Task<WaitingListTerms> PolicyAsync(long companyId, CancellationToken ct = default) => TermsAsync(companyId, ct);

    /// <inheritdoc />
    public async Task<Result> SetQualificationRulesAsync(long companyId, QualificationRules rules, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.CategoryIncomeCeilings.Any(c => c.Value < 0m || !Enum.IsDefined(c.Key)))
        {
            return Result.Failure(Error.Validation("Hse.Policy", "An income ceiling cannot be negative and must be for a known category."));
        }

        WaitingListPolicy? policy = await _db.Set<WaitingListPolicy>().FirstOrDefaultAsync(p => p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (policy is null)
        {
            policy = new WaitingListPolicy(companyId);
            _db.Set<WaitingListPolicy>().Add(policy);
        }

        policy.FirstTimeBuyersOnly = rules.FirstTimeBuyersOnly;
        policy.RefuseForfeited = rules.RefuseForfeited;
        policy.RequireRatesClearance = rules.RequireRatesClearance;
        policy.RentalFromWaitingList = rules.RentalFromWaitingList;
        List<WaitingListIncomeCeiling> ceilings = await _db.Set<WaitingListIncomeCeiling>().Where(c => c.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        _db.Set<WaitingListIncomeCeiling>().RemoveRange(ceilings.Where(c => !rules.CategoryIncomeCeilings.ContainsKey(c.Category)));
        foreach ((HousingCategory category, decimal ceiling) in rules.CategoryIncomeCeilings)
        {
            if (ceilings.FirstOrDefault(c => c.Category == category) is WaitingListIncomeCeiling found)
            {
                found.MaximumMonthlyIncome = ceiling;
            }
            else
            {
                _db.Set<WaitingListIncomeCeiling>().Add(new WaitingListIncomeCeiling(companyId, category, ceiling));
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<QualificationRules> QualificationRulesAsync(long companyId, CancellationToken ct = default)
    {
        var policy = await _db.Set<WaitingListPolicy>().AsNoTracking().Where(p => p.CompanyId == companyId)
            .Select(p => new { p.FirstTimeBuyersOnly, p.RefuseForfeited, p.RequireRatesClearance, p.RentalFromWaitingList }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        Dictionary<HousingCategory, decimal> ceilings = await _db.Set<WaitingListIncomeCeiling>().AsNoTracking().Where(c => c.CompanyId == companyId)
            .ToDictionaryAsync(c => c.Category, c => c.MaximumMonthlyIncome, ct).ConfigureAwait(false);
        return policy is null && ceilings.Count == 0
            ? QualificationRules.None
            : new QualificationRules(policy?.FirstTimeBuyersOnly ?? false, policy?.RefuseForfeited ?? false, policy?.RequireRatesClearance ?? false, ceilings,
                policy?.RentalFromWaitingList ?? false);
    }

    // Whether any of the household's accounts has ever held a council stand or owned property on the municipal register.
    private async Task<bool> EverHeldPropertyAsync(long companyId, List<long> customers, CancellationToken ct)
        => customers.Count > 0
            && (await _db.Set<HousingStand>().AnyAsync(s => s.CompanyId == companyId && (s.CurrentHolderCustomerId != null && customers.Contains(s.CurrentHolderCustomerId.Value)
                    || s.Ownership.Any(o => o.HolderCustomerId != null && customers.Contains(o.HolderCustomerId.Value))), ct).ConfigureAwait(false)
                || await _db.Set<PropertyOwner>().AnyAsync(o => o.CompanyId == companyId && customers.Contains(o.CustomerId), ct).ConfigureAwait(false));

    // Whether an instalment agreement of the household's ended in repossession or cancellation.
    private async Task<bool> ForfeitedAsync(long companyId, List<long> customers, CancellationToken ct)
    {
        if (customers.Count == 0)
        {
            return false;
        }

        List<InstalmentAgreement> agreements = await _db.Set<InstalmentAgreement>().AsNoTracking().Where(a => a.CompanyId == companyId && customers.Contains(a.BuyerCustomerId))
            .ToListAsync(ct).ConfigureAwait(false);
        if (agreements.Any(a => a.Status == InstalmentAgreementStatus.Repossessed))
        {
            return true;
        }

        List<string> numbers = [.. agreements.Select(a => a.AgreementNumber)];
        return numbers.Count > 0 && await _db.Set<AgreementCancellation>().AnyAsync(c => c.CompanyId == companyId && numbers.Contains(c.AgreementNumber), ct).ConfigureAwait(false);
    }

    // What the household owes on the receivables ledger past its due date.
    private async Task<decimal> OverdueAsync(long companyId, List<long> customers, DateOnly asAt, CancellationToken ct)
        => customers.Count == 0 ? 0m : await _db.OpenItems.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.Ledger == Core.Trading.TradingLedger.AccountsReceivable && customers.Contains(i.TraderId) && i.OutstandingBase != 0m)
            .SumAsync(i => i.Sign == Core.Trading.LedgerSign.Debit ? (i.DueDate < asAt ? i.OutstandingBase : 0m) : -i.OutstandingBase, ct).ConfigureAwait(false);

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
