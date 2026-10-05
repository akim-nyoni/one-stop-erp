using One4All.Core.Common;
using One4All.Core.Payroll;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>Dated leave and daily attendance (brief §14 O PR25, PR39–PR41, PR43).</summary>
public interface IAttendanceService
{
    /// <summary>Records leave taken between two dates and takes the days off the employee's balance (which must cover them).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="leaveType">The leave type.</param>
    /// <param name="fromDate">The first day.</param>
    /// <param name="toDate">The last day.</param>
    /// <param name="days">The working days (at most the calendar days in the range).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The record id, or an error.</returns>
    Task<Result<long>> TakeLeaveAsync(long companyId, long employeeId, string leaveType, DateOnly fromDate, DateOnly toDate, decimal days, CancellationToken ct = default);

    /// <summary>Pays out leave days instead of taking them, at a daily rate, reducing the balance.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="leaveType">The leave type.</param>
    /// <param name="date">The encashment date.</param>
    /// <param name="days">The days paid out.</param>
    /// <param name="dailyRate">The daily rate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The record id, or an error.</returns>
    Task<Result<long>> EncashLeaveAsync(long companyId, long employeeId, string leaveType, DateOnly date, decimal days, decimal dailyRate, CancellationToken ct = default);

    /// <summary>Records an employee's day (one record per employee per day).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="date">The day.</param>
    /// <param name="status">How it went.</param>
    /// <param name="clockIn">Clock-in.</param>
    /// <param name="clockOut">Clock-out.</param>
    /// <param name="lateMinutes">Minutes late.</param>
    /// <param name="overtimeHours">Overtime hours.</param>
    /// <param name="overtimeMultiplier">The overtime multiplier (≥ 1 when there is overtime).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The record id, or an error.</returns>
    Task<Result<long>> RecordDayAsync(long companyId, long employeeId, DateOnly date, AttendanceStatus status, TimeOnly? clockIn, TimeOnly? clockOut, int lateMinutes, decimal overtimeHours,
        decimal overtimeMultiplier, CancellationToken ct = default);

    /// <summary>Approves a day's overtime.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="attendanceId">The day.</param>
    /// <param name="approverUserId">The approver.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an error.</returns>
    Task<Result> ApproveOvertimeAsync(long companyId, long attendanceId, long approverUserId, CancellationToken ct = default);
}
