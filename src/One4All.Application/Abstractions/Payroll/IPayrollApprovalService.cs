using One4All.Core.Common;
using One4All.Core.Payroll;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>A payroll to calculate for approval.</summary>
public sealed record PayrollDraftRequest
{
    /// <summary>Gets the company.</summary>
    public long CompanyId { get; init; }

    /// <summary>Gets the pay group (blank for the default payroll).</summary>
    public string? PayGroup { get; init; }

    /// <summary>Gets the period.</summary>
    public string PeriodLabel { get; init; } = string.Empty;

    /// <summary>Gets the pay date.</summary>
    public DateOnly PayDate { get; init; }

    /// <summary>Gets the net-pay control account.</summary>
    public string NetPayAccountCode { get; init; } = string.Empty;

    /// <summary>Gets the run document number.</summary>
    public string DocumentNumber { get; init; } = string.Empty;

    /// <summary>Gets who is calculating.</summary>
    public long UserId { get; init; }
}

/// <summary>What the approver sees: the totals, the movement from the last run and the exception report.</summary>
/// <param name="DraftId">The draft.</param>
/// <param name="Status">Where it is in the workflow.</param>
/// <param name="Employees">Payslips calculated.</param>
/// <param name="Gross">Gross pay.</param>
/// <param name="Deductions">Deductions.</param>
/// <param name="Net">Net pay.</param>
/// <param name="PreviousNet">The net pay of the group's last run.</param>
/// <param name="MovementPercent">The movement in net pay from the last run.</param>
/// <param name="RequiresSecondApproval">Whether a second approver is needed.</param>
/// <param name="Exceptions">The exception report.</param>
public sealed record PayrollDraftResult(long DraftId, PayrollDraftStatus Status, int Employees, decimal Gross, decimal Deductions, decimal Net, decimal? PreviousNet, decimal? MovementPercent,
    bool RequiresSecondApproval, IReadOnlyList<string> Exceptions);

/// <summary>
/// Maker-checker payroll (brief §4.15.6): calculate (re-runnable any number of times - each calculation replaces the last), approve
/// by someone other than whoever calculated (and a second, different approver when net pay moves more than the company's threshold
/// from the group's last run), then commit - which recalculates and refuses if anything changed since approval.
/// </summary>
public interface IPayrollApprovalService
{
    /// <summary>Calculates the run and records it for approval, superseding any earlier calculation of the same group and period.</summary>
    /// <param name="request">The run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The totals, movement and exception report.</returns>
    Task<Result<PayrollDraftResult>> CalculateRunAsync(PayrollDraftRequest request, CancellationToken ct = default);

    /// <summary>
    /// Approves a calculated run. The approver cannot be whoever calculated it, nor the first approver when a second is needed
    /// (<c>Payroll.SameApprover</c>); only a calculated draft can be approved (<c>Payroll.DraftNotPending</c>).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="draftId">The draft.</param>
    /// <param name="approverUserId">The approver.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The draft as it now stands.</returns>
    Task<Result<PayrollDraftResult>> ApproveRunAsync(long companyId, long draftId, long approverUserId, CancellationToken ct = default);

    /// <summary>
    /// Commits an approved run: recalculates it and refuses if the totals no longer match what was approved
    /// (<c>Payroll.ChangedSinceApproval</c>), then saves the payslips and posts the journal.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="draftId">The draft.</param>
    /// <param name="userId">Who commits.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The run result.</returns>
    Task<Result<PayrollRunResult>> CommitRunAsync(long companyId, long draftId, long userId, CancellationToken ct = default);
}
