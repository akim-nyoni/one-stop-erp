using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A change of ownership to lodge.</summary>
public sealed class TransferLodgement
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The stand.</summary>
    public long StandId { get; init; }

    /// <summary>The kind of transfer.</summary>
    public TransferType TransferType { get; init; }

    /// <summary>The incoming holder's account.</summary>
    public long ToCustomerId { get; init; }

    /// <summary>The consideration.</summary>
    public decimal Consideration { get; init; }

    /// <summary>When it is lodged.</summary>
    public DateOnly Date { get; init; }
}

/// <summary>One checklist line of an application.</summary>
/// <param name="Requirement">The item.</param>
/// <param name="IsLive">Whether the system checks it.</param>
/// <param name="Satisfied">Whether it is satisfied now.</param>
/// <param name="Detail">The document relied on, or why a live check fails.</param>
public sealed record TransferChecklistLine(TransferRequirement Requirement, bool IsLive, bool Satisfied, string? Detail);

/// <summary>
/// Change of ownership and cession (brief §4.10.6): an application is lodged with its transfer type; each document item on
/// the type's checklist is verified by an officer against a document, while municipal clearance and the housing account are
/// checked live by the system; once complete it is submitted, approved by two officers (neither of whom verified it), and
/// completed - the stand's ownership chain and, where the stand is rated, the municipal owner register updated together.
/// </summary>
public interface ITransferApplicationService
{
    /// <summary>Lodges an application for a stand's current holder.</summary>
    /// <param name="lodgement">The transfer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The application number, or why it was refused.</returns>
    Task<Result<string>> LodgeAsync(TransferLodgement lodgement, CancellationToken ct = default);

    /// <summary>Records a document item verified by an officer (needs the ownership-change Edit permission).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="applicationNumber">The application.</param>
    /// <param name="requirement">The item.</param>
    /// <param name="documentReference">The document (for fees, the receipt number on the sales ledger).</param>
    /// <param name="userId">The officer.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> VerifyAsync(long companyId, string applicationNumber, TransferRequirement requirement, string documentReference, long userId, DateOnly date, CancellationToken ct = default);

    /// <summary>The application's checklist as it stands, live checks evaluated now.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="applicationNumber">The application.</param>
    /// <param name="date">The date to evaluate the live checks at.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The checklist, or why there is none.</returns>
    Task<Result<IReadOnlyList<TransferChecklistLine>>> ChecklistAsync(long companyId, string applicationNumber, DateOnly date, CancellationToken ct = default);

    /// <summary>Submits a complete application for approval (needs the ownership-change Edit permission).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="applicationNumber">The application.</param>
    /// <param name="userId">The officer.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or the items still outstanding.</returns>
    Task<Result> SubmitAsync(long companyId, string applicationNumber, long userId, DateOnly date, CancellationToken ct = default);

    /// <summary>Approves a submitted application; two different approvers, neither of whom verified or submitted it.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="applicationNumber">The application.</param>
    /// <param name="userId">The approver (needs the ownership-approval permission).</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> ApproveAsync(long companyId, string applicationNumber, long userId, DateOnly date, CancellationToken ct = default);

    /// <summary>Completes an approved transfer: the live checks are re-run and the registers updated together.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="applicationNumber">The application.</param>
    /// <param name="date">The effective date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> CompleteAsync(long companyId, string applicationNumber, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Records that the transferee formally assumes the transferor's housing balance (brief §4.10.6 pre-condition 2), approved by an officer with the ownership-approval
    /// permission who did not submit the application: the housing-account check then passes, and on completion the transferor's outstanding items move to the
    /// transferee through the clearing account (credit notes to the one, an invoice to the other, netting to nil) and any running instalment agreement on the stand
    /// gets the transferee as its buyer.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="applicationNumber">The application.</param>
    /// <param name="approverUserId">The approving officer.</param>
    /// <param name="clearingAccountCode">The account the balance passes through.</param>
    /// <param name="date">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The balance assumed, or why it was refused.</returns>
    Task<Result<decimal>> AssumeBalanceAsync(long companyId, string applicationNumber, long approverUserId, string clearingAccountCode, DateOnly date, CancellationToken ct = default);

    /// <summary>Rejects an open application.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="applicationNumber">The application.</param>
    /// <param name="reason">Why.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RejectAsync(long companyId, string applicationNumber, string reason, DateOnly date, CancellationToken ct = default);
}
