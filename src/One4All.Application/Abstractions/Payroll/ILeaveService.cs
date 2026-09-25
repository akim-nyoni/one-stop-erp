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
}
