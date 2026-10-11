using One4All.Core.Common;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A stand in a cooperative's allocation.</summary>
/// <param name="StandNumber">The stand.</param>
/// <param name="AllocatedOn">When it was allocated to the cooperative.</param>
/// <param name="MemberName">The member it was sub-allocated to, if any.</param>
/// <param name="SubAllocatedOn">When.</param>
public sealed record CooperativeStandEntry(string StandNumber, DateOnly AllocatedOn, string? MemberName, DateOnly? SubAllocatedOn);

/// <summary>A line of a cooperative's account statement.</summary>
/// <param name="Date">The document date.</param>
/// <param name="Reference">The document number.</param>
/// <param name="Description">What it was.</param>
/// <param name="Debit">Charged to the cooperative.</param>
/// <param name="Credit">Paid or credited.</param>
/// <param name="Balance">The running balance owed.</param>
public sealed record CooperativeStatementLine(DateOnly Date, string Reference, string Description, decimal Debit, decimal Credit, decimal Balance);

/// <summary>A member's contributions in a statement period.</summary>
/// <param name="IdNumber">The member's national ID.</param>
/// <param name="FullName">The member.</param>
/// <param name="Total">What they contributed.</param>
public sealed record MemberContributions(string IdNumber, string FullName, decimal Total);

/// <summary>A cooperative's account statement.</summary>
/// <param name="Cooperative">The cooperative's code and name.</param>
/// <param name="From">The first day.</param>
/// <param name="To">The last day.</param>
/// <param name="Opening">The balance owed before the period.</param>
/// <param name="Lines">The documents in the period with the running balance.</param>
/// <param name="Closing">The balance owed at the end.</param>
/// <param name="Contributions">Each member's contributions in the period.</param>
public sealed record CooperativeStatement(string Cooperative, DateOnly From, DateOnly To, decimal Opening, IReadOnlyList<CooperativeStatementLine> Lines, decimal Closing,
    IReadOnlyList<MemberContributions> Contributions);

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

    /// <summary>Links the cooperative to its receivables account with the council (a customer not already linked to another cooperative).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="cooperativeCode">The cooperative.</param>
    /// <param name="customerId">The customer account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> LinkAccountAsync(long companyId, string cooperativeCode, long customerId, CancellationToken ct = default);

    /// <summary>Charges the cooperative (stand price, service or administration charge): an invoice on its account.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="cooperativeCode">The cooperative.</param>
    /// <param name="date">The date.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="incomeAccountCode">The income (or stands-available) account credited.</param>
    /// <param name="reference">The document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> ChargeAsync(long companyId, string cooperativeCode, DateOnly date, decimal amount, string incomeAccountCode, string reference, CancellationToken ct = default);

    /// <summary>Records an active member's contribution: receipted on the cooperative's account and kept against the member. All or nothing.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="cooperativeCode">The cooperative.</param>
    /// <param name="memberIdNumber">The member's national ID.</param>
    /// <param name="date">The date.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="bankAccountCode">The bank account it was paid into.</param>
    /// <param name="reference">The receipt number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RecordContributionAsync(long companyId, string cooperativeCode, string memberIdNumber, DateOnly date, decimal amount, string bankAccountCode, string reference,
        CancellationToken ct = default);

    /// <summary>The cooperative's own account statement for a period, with each member's contributions.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="cooperativeCode">The cooperative.</param>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The statement, or why there is none.</returns>
    Task<Result<CooperativeStatement>> StatementAsync(long companyId, string cooperativeCode, DateOnly from, DateOnly to, CancellationToken ct = default);
}
