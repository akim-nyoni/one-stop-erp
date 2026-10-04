using One4All.Core.Common;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A stand in a cooperative's allocation.</summary>
/// <param name="StandNumber">The stand.</param>
/// <param name="AllocatedOn">When it was allocated to the cooperative.</param>
/// <param name="MemberName">The member it was sub-allocated to, if any.</param>
/// <param name="SubAllocatedOn">When.</param>
public sealed record CooperativeStandEntry(string StandNumber, DateOnly AllocatedOn, string? MemberName, DateOnly? SubAllocatedOn);

/// <summary>
/// Cooperative allocations (brief §4.10.5: allocation to a cooperative, which sub-allocates to members — track both
/// levels): register cooperatives and members, allocate available stands to a cooperative, sub-allocate each to an
/// active member (one stand each), and show the two-level register.
/// </summary>
public interface ICooperativeService
{
    /// <summary>Registers a cooperative.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The code (unique).</param>
    /// <param name="name">The name.</param>
    /// <param name="registrationNumber">The registration number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cooperative, or why it was refused.</returns>
    Task<Result<long>> RegisterAsync(long companyId, string code, string name, string registrationNumber, CancellationToken ct = default);

    /// <summary>Adds a member; a national ID can appear once in a cooperative.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="cooperativeCode">The cooperative.</param>
    /// <param name="idNumber">The member's national ID.</param>
    /// <param name="fullName">The member's name.</param>
    /// <param name="joinedOn">When they joined.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> AddMemberAsync(long companyId, string cooperativeCode, string idNumber, string fullName, DateOnly joinedOn, CancellationToken ct = default);

    /// <summary>Records that a member has left.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="cooperativeCode">The cooperative.</param>
    /// <param name="idNumber">The member's national ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RemoveMemberAsync(long companyId, string cooperativeCode, string idNumber, CancellationToken ct = default);

    /// <summary>Allocates available stands, not already allocated, to a cooperative, marking them allocated. One unit.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="cooperativeCode">The cooperative.</param>
    /// <param name="standIds">The stands.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number allocated, or why they were refused.</returns>
    Task<Result<int>> AllocateStandsAsync(long companyId, string cooperativeCode, IReadOnlyList<long> standIds, DateOnly date, CancellationToken ct = default);

    /// <summary>Sub-allocates one of the cooperative's stands to one of its active members, who holds no other.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="cooperativeCode">The cooperative.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="memberIdNumber">The member's national ID.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SubAllocateAsync(long companyId, string cooperativeCode, long standId, string memberIdNumber, DateOnly date, CancellationToken ct = default);

    /// <summary>The cooperative's stands and who holds each.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="cooperativeCode">The cooperative.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The two-level register, or why there is none.</returns>
    Task<Result<IReadOnlyList<CooperativeStandEntry>>> StandsAsync(long companyId, string cooperativeCode, CancellationToken ct = default);
}
