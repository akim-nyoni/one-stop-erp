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

    /// <summary>
    /// Cancels leave not yet begun (all its days back to the balance), or cuts it short when the employee is recalled: it ends the day before
    /// they return and the days not taken go back (pro rata to the calendar days unless given).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="leaveRecordId">The leave taken.</param>
    /// <param name="returnedOn">The day back at work (null or on/before the start: cancelled outright).</param>
    /// <param name="daysBack">The working days given back when curtailed (null: pro rata).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The days given back.</returns>
    Task<Result<decimal>> CancelLeaveAsync(long companyId, long leaveRecordId, DateOnly? returnedOn, decimal? daysBack = null, CancellationToken ct = default);

    /// <summary>Who is on leave between two dates (brief §4.15 leave calendar), optionally for one department.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <param name="department">The department (null: all).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The leave in the window, by start date.</returns>
    Task<IReadOnlyList<LeaveCalendarEntry>> LeaveCalendarAsync(long companyId, DateOnly from, DateOnly to, string? department = null, CancellationToken ct = default);

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

    /// <summary>
    /// Imports a time-clock export (brief §4.15): each employee's punches paired into days (first in, last out) and recorded as present, late
    /// against the rostered shift, with the hours beyond the shift as overtime awaiting approval. Days already recorded are left alone; unknown
    /// badges and unreadable lines are reported. All or nothing.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="export">The export (badge,date time per line).</param>
    /// <param name="overtimeMultiplier">The overtime rate (1.5 = time and a half).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was imported.</returns>
    Task<Result<ClockImport>> ImportClockAsync(long companyId, string export, decimal overtimeMultiplier = 1.5m, CancellationToken ct = default);
}

/// <summary>A time-clock import.</summary>
/// <param name="DaysRecorded">Days recorded.</param>
/// <param name="AlreadyRecorded">Days left alone because they were recorded before.</param>
/// <param name="UnknownBadges">Badges matching no employee.</param>
/// <param name="Problems">Lines that could not be read.</param>
public sealed record ClockImport(int DaysRecorded, int AlreadyRecorded, IReadOnlyList<string> UnknownBadges, IReadOnlyList<string> Problems);

/// <summary>A period of leave on the calendar.</summary>
/// <param name="LeaveRecordId">The leave record.</param>
/// <param name="EmployeeNumber">The employee.</param>
/// <param name="Name">Their name.</param>
/// <param name="Department">Their department.</param>
/// <param name="LeaveType">The leave type.</param>
/// <param name="From">The first day.</param>
/// <param name="To">The last day.</param>
/// <param name="Days">The working days.</param>
public sealed record LeaveCalendarEntry(long LeaveRecordId, string EmployeeNumber, string Name, string? Department, string LeaveType, DateOnly From, DateOnly To, decimal Days);
