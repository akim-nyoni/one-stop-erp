using One4All.Core.Common;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>Tracks employee leave balances (accrual and days taken). Operational HR — no GL.</summary>
public interface ILeaveService
{
    /// <summary>Accrues leave days of a type for an employee (creating the balance if needed).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="leaveType">The leave type (e.g. ANNUAL, SICK).</param>
    /// <param name="days">The days to accrue (positive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The balance available after accrual.</returns>
    Task<Result<decimal>> AccrueAsync(long companyId, long employeeId, string leaveType, decimal days, CancellationToken ct = default);

    /// <summary>
    /// Records leave taken for an employee, reducing the available balance. Refuses more than the available
    /// balance.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="leaveType">The leave type.</param>
    /// <param name="days">The days taken (positive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The balance available after the leave is taken.</returns>
    Task<Result<decimal>> TakeAsync(long companyId, long employeeId, string leaveType, decimal days, CancellationToken ct = default);

    /// <summary>
    /// Trues up the leave provision (IAS 19) to the value of all outstanding leave (total balance days × the
    /// daily rate): posts the difference against the provision liability — an increase debits leave expense and
    /// credits the provision, a decrease releases it back. A nil change posts nothing. Idempotent across
    /// periods: re-running only posts the movement to the new target.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="date">The provision date.</param>
    /// <param name="dailyRate">The value of one leave day.</param>
    /// <param name="leaveExpenseAccountCode">The leave-expense account.</param>
    /// <param name="leaveProvisionAccountCode">The leave-provision liability account.</param>
    /// <param name="documentNumber">The provision document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The provision level (target liability) after the true-up.</returns>
    Task<Result<decimal>> RaiseLeaveProvisionAsync(long companyId, DateOnly date, decimal dailyRate, string leaveExpenseAccountCode, string leaveProvisionAccountCode, string documentNumber, CancellationToken ct = default);
}
