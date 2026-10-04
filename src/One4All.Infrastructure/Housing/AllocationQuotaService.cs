using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Allocation quotas per beneficiary group, and applicants' declared groups (brief §14 H37).</summary>
public sealed class AllocationQuotaService : IAllocationQuotaService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public AllocationQuotaService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SetQuotaAsync(long companyId, string? schemeCode, BeneficiaryGroups group, decimal targetPercent, CancellationToken ct = default)
    {
        if (!QuotaGroups.All.Contains(group) || targetPercent is < 0m or > 100m)
        {
            return Result.Failure(Error.Validation("Hse.QuotaDetails", "A quota is for one beneficiary group, at 0–100% of allocations."));
        }

        long? schemeId = null;
        if (!string.IsNullOrWhiteSpace(schemeCode))
        {
            schemeId = await _db.HousingSchemes.Where(s => s.CompanyId == companyId && s.Code == schemeCode.Trim()).Select(s => (long?)s.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (schemeId is null)
            {
                return Result.Failure(Error.NotFound("Hse.SchemeNotFound", $"Scheme {schemeCode} not found."));
            }
        }

        AllocationQuota? quota = await _db.Set<AllocationQuota>().FirstOrDefaultAsync(q => q.CompanyId == companyId && q.SchemeId == schemeId && q.Group == group, ct).ConfigureAwait(false);
        if (quota is null)
        {
            _db.Set<AllocationQuota>().Add(new AllocationQuota(companyId, schemeId, group, targetPercent));
        }
        else
        {
            quota.TargetPercent = targetPercent;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> DeclareGroupsAsync(long companyId, string waitingListNumber, BeneficiaryGroups groups, CancellationToken ct = default)
    {
        if ((groups & ~QuotaGroups.All.Aggregate(BeneficiaryGroups.None, (a, g) => a | g)) != 0)
        {
            return Result.Failure(Error.Validation("Hse.QuotaGroups", "Unknown beneficiary group."));
        }

        HousingApplicant? applicant = await _db.HousingApplicants.FirstOrDefaultAsync(a => a.CompanyId == companyId && a.WaitingListNumber == waitingListNumber, ct).ConfigureAwait(false);
        if (applicant is null)
        {
            return Result.Failure(Error.NotFound("Hse.ApplicantNotFound", $"Applicant {waitingListNumber} not found."));
        }

        applicant.DeclareGroups(groups);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
