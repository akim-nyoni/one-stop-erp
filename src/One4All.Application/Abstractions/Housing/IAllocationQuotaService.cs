using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>
/// Allocation quota policy (brief §14 H37): the share of allocations reserved for each beneficiary group, council-wide or
/// for one scheme, and the groups each applicant declared and proved.
/// </summary>
public interface IAllocationQuotaService
{
    /// <summary>Sets (or replaces) a group's quota.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="schemeCode">The scheme, or null for every scheme.</param>
    /// <param name="group">One group.</param>
    /// <param name="targetPercent">The share reserved, 0–100.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> SetQuotaAsync(long companyId, string? schemeCode, BeneficiaryGroups group, decimal targetPercent, CancellationToken ct = default);

    /// <summary>Records the groups an applicant declared and proved.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="waitingListNumber">The applicant.</param>
    /// <param name="groups">The groups.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> DeclareGroupsAsync(long companyId, string waitingListNumber, BeneficiaryGroups groups, CancellationToken ct = default);
}
