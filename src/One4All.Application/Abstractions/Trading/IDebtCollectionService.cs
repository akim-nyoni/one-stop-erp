using One4All.Core.Common;
using One4All.Core.Trading;

namespace One4All.Application.Abstractions.Trading;

/// <summary>A summons on the register.</summary>
/// <param name="CaseNumber">The case.</param>
/// <param name="CustomerCode">The debtor.</param>
/// <param name="SummonsNumber">The summons number.</param>
/// <param name="Court">The court.</param>
/// <param name="IssuedOn">When it was issued.</param>
/// <param name="HearingDate">The hearing date.</param>
/// <param name="AmountHandedOver">The debt.</param>
public sealed record SummonsEntry(string CaseNumber, string CustomerCode, string SummonsNumber, string Court, DateOnly IssuedOn, DateOnly? HearingDate, decimal AmountHandedOver);

/// <summary>The result of an approved write-off.</summary>
/// <param name="CaseNumber">The case.</param>
/// <param name="InvoicesWrittenOff">Invoices written off.</param>
/// <param name="Amount">The amount written off.</param>
public sealed record CaseWriteOff(string CaseNumber, int InvoicesWrittenOff, decimal Amount);

/// <summary>
/// Legal handover of debts, the summons register and write-off approval (brief §4.9): an overdue debtor is handed over
/// to a collector, a summons is recorded, and a case is either closed (settled or withdrawn) or written off — requested
/// by one user, approved by a different one, which writes off every outstanding invoice of the debtor in one unit.
/// </summary>
public interface IDebtCollectionService
{
    /// <summary>Hands a debtor over; one open case per debtor, and only with an overdue balance.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The debtor.</param>
    /// <param name="handedOverTo">The collector or attorney.</param>
    /// <param name="date">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The case number, or why it was refused.</returns>
    Task<Result<string>> HandOverAsync(long companyId, long customerId, string handedOverTo, DateOnly date, CancellationToken ct = default);

    /// <summary>Records a summons on an open case.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="caseNumber">The case.</param>
    /// <param name="summonsNumber">The summons number.</param>
    /// <param name="court">The court.</param>
    /// <param name="issuedOn">When it was issued.</param>
    /// <param name="hearingDate">The hearing date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RecordSummonsAsync(long companyId, string caseNumber, string summonsNumber, string court, DateOnly issuedOn, DateOnly? hearingDate, CancellationToken ct = default);

    /// <summary>The summons register, by issue date.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The summonses.</returns>
    Task<IReadOnlyList<SummonsEntry>> SummonsRegisterAsync(long companyId, CancellationToken ct = default);

    /// <summary>Asks for an open case to be written off.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="caseNumber">The case.</param>
    /// <param name="userId">Who asks.</param>
    /// <param name="reason">Why; required.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> RequestWriteOffAsync(long companyId, string caseNumber, long userId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Asks for an open case to be written off to a bad-debt account; when the company has a write-off workflow, the request goes through
    /// it (approval writes the debt off on the day it is approved, rejection withdraws the request) and cannot be approved directly.
    /// </summary>
    Task<Result> RequestWriteOffAsync(long companyId, string caseNumber, long userId, string reason, string badDebtAccountCode, CancellationToken ct = default);

    /// <summary>Approves a requested write-off — never by the requester — writing off the debtor's outstanding invoices.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="caseNumber">The case.</param>
    /// <param name="approverUserId">The approver.</param>
    /// <param name="date">The write-off date.</param>
    /// <param name="badDebtAccountCode">The bad debt expense account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was written off, or why it was refused.</returns>
    Task<Result<CaseWriteOff>> ApproveWriteOffAsync(long companyId, string caseNumber, long approverUserId, DateOnly date, string badDebtAccountCode, CancellationToken ct = default);

    /// <summary>Closes an open case as settled or withdrawn.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="caseNumber">The case.</param>
    /// <param name="outcome">Settled or withdrawn.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> CloseAsync(long companyId, string caseNumber, DebtCaseStage outcome, DateOnly date, CancellationToken ct = default);
}
