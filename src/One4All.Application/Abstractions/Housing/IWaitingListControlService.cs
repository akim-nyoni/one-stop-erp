using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>The result of a renewal run.</summary>
/// <param name="Suspended">Applicants suspended for not confirming.</param>
/// <param name="Removed">Applicants removed after the grace period.</param>
/// <param name="Reminded">Applicants reminded that their renewal falls due.</param>
/// <param name="ByLetter">Notices to send by letter (no phone number on file).</param>
public sealed record RenewalRunResult(int Suspended, int Removed, int Reminded = 0, int ByLetter = 0);

/// <summary>A notice in an applicant's history.</summary>
/// <param name="NoticeType">The kind of notice.</param>
/// <param name="SentOn">When it was sent.</param>
/// <param name="Channel">How it went out (SMS or Letter).</param>
/// <param name="Recipient">Where it went.</param>
public sealed record ApplicantNoticeEntry(ApplicantNoticeType NoticeType, DateOnly SentOn, string? Channel = null, string? Recipient = null);

/// <summary>
/// Waiting-list integrity controls (brief §4.10.3): dual-authorised registration-date changes, renewal confirmations,
/// the renewal run that suspends and then removes non-responders, and each applicant's notice history.
/// </summary>
public interface IWaitingListControlService
{
    /// <summary>Requests a change of registration date; nothing changes until a second officer approves.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="waitingListNumber">The applicant.</param>
    /// <param name="newDate">The date asked for.</param>
    /// <param name="reason">Why (required).</param>
    /// <param name="userId">Who asks.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The request, or why it was refused.</returns>
    Task<Result<long>> RequestDateChangeAsync(long companyId, string waitingListNumber, DateOnly newDate, string reason, long userId, CancellationToken ct = default);

    /// <summary>Approves or rejects a requested change; the approver cannot be the requester. Approval moves the date.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="requestId">The request.</param>
    /// <param name="approve">Approve or reject.</param>
    /// <param name="userId">The deciding officer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> DecideDateChangeAsync(long companyId, long requestId, bool approve, long userId, CancellationToken ct = default);

    /// <summary>Records an applicant's renewal confirmation, reinstating a suspended applicant.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="waitingListNumber">The applicant.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> ConfirmAsync(long companyId, string waitingListNumber, DateOnly date, CancellationToken ct = default);

    /// <summary>Runs the renewal cycle: overdue applicants are suspended and, after the grace period, removed, each with a notice.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="asAtDate">The run date.</param>
    /// <param name="cycleMonths">How often applicants must confirm.</param>
    /// <param name="graceDays">Days after renewal falls due before removal.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What the run did.</returns>
    Task<RenewalRunResult> RunRenewalsAsync(long companyId, DateOnly asAtDate, int cycleMonths, int graceDays, CancellationToken ct = default);

    /// <summary>An applicant's notice history, oldest first.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="waitingListNumber">The applicant.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The notices.</returns>
    Task<IReadOnlyList<ApplicantNoticeEntry>> NoticesAsync(long companyId, string waitingListNumber, CancellationToken ct = default);
}
