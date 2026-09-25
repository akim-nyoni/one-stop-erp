using One4All.Core.Common;

namespace One4All.Application.Abstractions.Expenses;

/// <summary>One line of an expense claim to create: an expense account and amount with a description.</summary>
/// <param name="Description">The line description.</param>
/// <param name="ExpenseAccountCode">The expense account debited on reimbursement.</param>
/// <param name="Amount">The amount (positive).</param>
public readonly record struct ExpenseClaimLineRequest(string Description, string ExpenseAccountCode, decimal Amount);

/// <summary>
/// Manages employee expense claims: capture, approval and reimbursement. Approval gates payment, and
/// reimbursement posts the expenses (Dr each line's account) against a funding account (Cr) through the
/// central posting engine — no expense reaches the ledger until the claim is paid.
/// </summary>
public interface IExpenseClaimService
{
    /// <summary>Creates a draft claim with its lines.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The claimant employee.</param>
    /// <param name="reference">The claim reference.</param>
    /// <param name="claimDate">The claim date.</param>
    /// <param name="lines">The expense lines (at least one, each with a positive amount).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created claim id.</returns>
    Task<Result<long>> CreateAsync(long companyId, long employeeId, string reference, DateOnly claimDate, IReadOnlyList<ExpenseClaimLineRequest> lines, CancellationToken ct = default);

    /// <summary>Approves a draft claim for payment.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="claimId">The claim.</param>
    /// <param name="approvedByUserId">The approver.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result> ApproveAsync(long companyId, long claimId, long approvedByUserId, CancellationToken ct = default);

    /// <summary>Rejects a draft claim.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="claimId">The claim.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result> RejectAsync(long companyId, long claimId, CancellationToken ct = default);

    /// <summary>
    /// Reimburses an approved claim: posts Dr each expense line's account / Cr the funding account for the
    /// claim total and marks the claim reimbursed. Refuses a claim that is not approved.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="claimId">The claim.</param>
    /// <param name="date">The reimbursement date.</param>
    /// <param name="fundingAccountCode">The bank/cash account the reimbursement is paid from (credited).</param>
    /// <param name="documentNumber">The payment document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The reimbursement journal number.</returns>
    Task<Result<string>> ReimburseAsync(long companyId, long claimId, DateOnly date, string fundingAccountCode, string documentNumber, CancellationToken ct = default);
}
