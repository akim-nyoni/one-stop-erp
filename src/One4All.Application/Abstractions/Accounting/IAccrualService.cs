using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>The two journals produced by a reversing entry: the period-end posting and its auto-reversal.</summary>
/// <param name="PostingJournalNumber">The period-end journal (accrual or prepayment).</param>
/// <param name="ReversalJournalNumber">The auto-reversal journal posted in the following period.</param>
public readonly record struct ReversingJournalResult(string PostingJournalNumber, string ReversalJournalNumber);

/// <summary>A prepayment to release to expense month by month.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="Reference">The reference (unique).</param>
/// <param name="Description">What was prepaid.</param>
/// <param name="PrepaymentAccountCode">The prepayment (asset) account it sits in.</param>
/// <param name="ExpenseAccountCode">The expense account charged each month.</param>
/// <param name="Total">The amount to release.</param>
/// <param name="FirstMonth">The first month covered (any day in it).</param>
/// <param name="Months">How many months it covers (1 to 120).</param>
/// <param name="CostCentre">The cost centre charged.</param>
/// <param name="Department">The department charged.</param>
public sealed record NewPrepaymentSchedule(long CompanyId, string Reference, string Description, string PrepaymentAccountCode, string ExpenseAccountCode, decimal Total, DateOnly FirstMonth,
    int Months, string? CostCentre = null, string? Department = null);

/// <summary>A release run's outcome.</summary>
/// <param name="Schedules">Schedules that released something.</param>
/// <param name="Months">Months released.</param>
/// <param name="Amount">The amount charged to expense.</param>
/// <param name="Journals">The journals posted.</param>
public sealed record PrepaymentRelease(int Schedules, int Months, decimal Amount, IReadOnlyList<string> Journals);

/// <summary>
/// Posts auto-reversing period-end journals - accruals and prepayments - through the central posting engine.
/// Each call posts the period-end entry and immediately posts its exact inverse dated in the next period (via
/// <see cref="IPostingEngine.ReverseAsync"/>), so the two net to zero once the actual invoice lands, the classic
/// month-end accrual/prepayment treatment with no manual reversal to remember.
/// </summary>
public interface IAccrualService
{
    /// <summary>Sets up a prepayment to be released to expense over the months it covers (equal monthly amounts, the last month taking the rounding).</summary>
    /// <param name="schedule">The schedule.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The schedule's id, or why not.</returns>
    Task<Result<long>> CreatePrepaymentScheduleAsync(NewPrepaymentSchedule schedule, CancellationToken ct = default);

    /// <summary>
    /// Releases every month of every prepayment schedule that has ended on or before the date and is not yet released: Dr the expense, Cr the
    /// prepayment, one journal a month dated its last day. Run again it releases nothing twice.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAt">Release the months ending on or before this date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was released.</returns>
    Task<Result<PrepaymentRelease>> ReleasePrepaymentsAsync(long companyId, DateOnly asAt, CancellationToken ct = default);

    /// <summary>
    /// Posts an accrual for an expense incurred but not yet invoiced: Dr the expense account / Cr the accrual
    /// (liability) account on <paramref name="date"/>, auto-reversed on <paramref name="reversalDate"/>.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="date">The period-end posting date.</param>
    /// <param name="reversalDate">The reversal date (must be after <paramref name="date"/>).</param>
    /// <param name="expenseAccountCode">The expense account (debited at period end).</param>
    /// <param name="accrualAccountCode">The accrual liability account (credited at period end).</param>
    /// <param name="amount">The amount to accrue (positive).</param>
    /// <param name="documentNumber">The document number.</param>
    /// <param name="narrative">The journal narrative.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The accrual and reversal journal numbers.</returns>
    Task<Result<ReversingJournalResult>> PostAccrualAsync(long companyId, DateOnly date, DateOnly reversalDate, string expenseAccountCode, string accrualAccountCode, decimal amount, string documentNumber, string narrative, CancellationToken ct = default);

    /// <summary>
    /// Posts a prepayment for an expense paid in advance: Dr the prepayment (asset) account / Cr the expense
    /// account on <paramref name="date"/> (deferring the cost), auto-reversed on <paramref name="reversalDate"/>
    /// to release the cost into the next period.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="date">The period-end posting date.</param>
    /// <param name="reversalDate">The reversal date (must be after <paramref name="date"/>).</param>
    /// <param name="prepaymentAccountCode">The prepayment asset account (debited at period end).</param>
    /// <param name="expenseAccountCode">The expense account (credited at period end, released on reversal).</param>
    /// <param name="amount">The amount to defer (positive).</param>
    /// <param name="documentNumber">The document number.</param>
    /// <param name="narrative">The journal narrative.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The prepayment and reversal journal numbers.</returns>
    Task<Result<ReversingJournalResult>> PostPrepaymentAsync(long companyId, DateOnly date, DateOnly reversalDate, string prepaymentAccountCode, string expenseAccountCode, decimal amount, string documentNumber, string narrative, CancellationToken ct = default);
}
