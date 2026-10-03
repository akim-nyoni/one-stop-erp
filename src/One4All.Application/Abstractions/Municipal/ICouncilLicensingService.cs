using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>A request to issue a council licence or permit.</summary>
public sealed class IssueLicenceRequest
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The kind of licence.</summary>
    public CouncilLicenceKind Kind { get; init; }

    /// <summary>The holder's customer account.</summary>
    public long HolderCustomerId { get; init; }

    /// <summary>The property it is for, if any.</summary>
    public long? PropertyId { get; init; }

    /// <summary>The trading name, premises or works it covers.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The issue date; the first cycle runs from here.</summary>
    public DateOnly IssuedOn { get; init; }

    /// <summary>The months in a renewal cycle (12 for an annual licence).</summary>
    public int CycleMonths { get; init; } = 12;

    /// <summary>The fee per cycle, invoiced on issue and on each renewal.</summary>
    public decimal Fee { get; init; }

    /// <summary>The revenue account the fee is credited to.</summary>
    public string RevenueAccountCode { get; init; } = string.Empty;
}

/// <summary>A licence that has been issued.</summary>
/// <param name="LicenceNumber">The licence number.</param>
/// <param name="ExpiresOn">The last day it is valid.</param>
/// <param name="FeeInvoiceNumber">The fee invoice, if a fee was charged.</param>
public sealed record IssuedLicence(string LicenceNumber, DateOnly ExpiresOn, string? FeeInvoiceNumber);

/// <summary>A renewal that has been made.</summary>
/// <param name="Terms">The cycles, expiry, fee and penalty.</param>
/// <param name="InvoiceNumber">The renewal invoice, if anything was charged.</param>
public sealed record LicenceRenewed(RenewalTerms Terms, string? InvoiceNumber);

/// <summary>A licence expiring soon or already expired.</summary>
/// <param name="LicenceNumber">The licence number.</param>
/// <param name="Kind">The kind of licence.</param>
/// <param name="HolderCode">The holder's customer code.</param>
/// <param name="Description">What it covers.</param>
/// <param name="ExpiresOn">The last day it is valid.</param>
/// <param name="DaysLeft">Days left from the as-at date (negative once expired).</param>
public sealed record ExpiringLicence(string LicenceNumber, CouncilLicenceKind Kind, string HolderCode, string Description, DateOnly ExpiresOn, int DaysLeft);

/// <summary>
/// Council licensing and permits (brief §4.9): issue a licence and invoice its fee to the holder, inspect it (a failure
/// suspends it until an inspection passes), renew it in fixed cycles with a penalty when late, list renewals due, and
/// revoke it.
/// </summary>
public interface ICouncilLicensingService
{
    /// <summary>Issues a licence, invoicing the first cycle's fee to the holder, in one unit.</summary>
    /// <param name="request">The licence.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The licence, or why it could not be issued.</returns>
    Task<Result<IssuedLicence>> IssueAsync(IssueLicenceRequest request, CancellationToken ct = default);

    /// <summary>Records an inspection: a failure suspends the licence, a pass reinstates a suspended one.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="licenceNumber">The licence.</param>
    /// <param name="date">The inspection date.</param>
    /// <param name="inspector">Who inspected.</param>
    /// <param name="passed">Whether it passed.</param>
    /// <param name="notes">Findings (required for a failure).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The licence's status afterwards, or why it could not be recorded.</returns>
    Task<Result<CouncilLicenceStatus>> RecordInspectionAsync(long companyId, string licenceNumber, DateOnly date, string inspector, bool passed, string? notes, CancellationToken ct = default);

    /// <summary>Renews a licence, invoicing the fee for each cycle due and any late penalty, in one unit.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="licenceNumber">The licence.</param>
    /// <param name="date">The renewal date.</param>
    /// <param name="penaltyPercent">The late penalty as a percentage of the fee.</param>
    /// <param name="penaltyAccountCode">The account the penalty is credited to (needed when one is charged).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The renewal, or why it was refused.</returns>
    Task<Result<LicenceRenewed>> RenewAsync(long companyId, string licenceNumber, DateOnly date, decimal penaltyPercent, string? penaltyAccountCode, CancellationToken ct = default);

    /// <summary>Lists licences in force or suspended that expire within the window, or have expired, soonest first.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="asAtDate">The as-at date.</param>
    /// <param name="withinDays">The window.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The licences.</returns>
    Task<IReadOnlyList<ExpiringLicence>> RenewalsDueAsync(long companyId, DateOnly asAtDate, int withinDays, CancellationToken ct = default);

    /// <summary>Revokes a licence for good.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="licenceNumber">The licence.</param>
    /// <param name="reason">Why (required).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it could not be revoked.</returns>
    Task<Result> RevokeAsync(long companyId, string licenceNumber, string reason, CancellationToken ct = default);
}
