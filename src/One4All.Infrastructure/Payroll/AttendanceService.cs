using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <summary>Records dated leave (through the leave balances) and daily attendance.</summary>
public sealed class AttendanceService : IAttendanceService
{
    private readonly One4AllDbContext _db;
    private readonly ILeaveService _leave;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="leave">Moves the leave balances.</param>
    public AttendanceService(One4AllDbContext db, ILeaveService leave)
    {
        _db = db;
        _leave = leave;
    }

    /// <inheritdoc />
    public Task<Result<long>> TakeLeaveAsync(long companyId, long employeeId, string leaveType, DateOnly fromDate, DateOnly toDate, decimal days, CancellationToken ct = default)
        => _db.AtomicAsync(() => TakeCoreAsync(companyId, employeeId, leaveType, fromDate, toDate, days, ct), ct);

    // One unit of work: the balance and the dated record move together.
    private async Task<Result<long>> TakeCoreAsync(long companyId, long employeeId, string leaveType, DateOnly fromDate, DateOnly toDate, decimal days, CancellationToken ct)
    {
        if (toDate < fromDate || days <= 0m || days > toDate.DayNumber - fromDate.DayNumber + 1)
        {
            return Error.Validation("Hr.LeaveDates", "Leave needs an end on or after its start and positive days no more than the days in the range.");
        }

        if (await _db.LeaveRecords.AnyAsync(l => l.EmployeeId == employeeId && l.Kind == LeaveRecordKind.Taken && l.FromDate <= toDate && l.ToDate >= fromDate, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hr.LeaveOverlap", "The employee already has leave in those dates.");
        }

        Result<decimal> taken = await _leave.TakeAsync(companyId, employeeId, leaveType, days, ct).ConfigureAwait(false);
        if (taken.IsFailure)
        {
            return taken.Error;
        }

        LeaveRecord record = new(companyId, employeeId, leaveType.Trim(), LeaveRecordKind.Taken, fromDate, toDate, days, 0m);
        _db.LeaveRecords.Add(record);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return record.Id;
    }

    /// <inheritdoc />
    public Task<Result<long>> EncashLeaveAsync(long companyId, long employeeId, string leaveType, DateOnly date, decimal days, decimal dailyRate, CancellationToken ct = default)
        => _db.AtomicAsync(() => EncashCoreAsync(companyId, employeeId, leaveType, date, days, dailyRate, ct), ct);

    // One unit of work: the balance and the encashment record move together.
    private async Task<Result<long>> EncashCoreAsync(long companyId, long employeeId, string leaveType, DateOnly date, decimal days, decimal dailyRate, CancellationToken ct)
    {
        if (dailyRate <= 0m)
        {
            return Error.Validation("Hr.EncashRate", "An encashment needs a positive daily rate.");
        }

        Result<decimal> taken = await _leave.TakeAsync(companyId, employeeId, leaveType, days, ct).ConfigureAwait(false);
        if (taken.IsFailure)
        {
            return taken.Error;
        }

        LeaveRecord record = new(companyId, employeeId, leaveType.Trim(), LeaveRecordKind.Encashed, date, date, days, Math.Round(days * dailyRate, 2, MidpointRounding.AwayFromZero));
        _db.LeaveRecords.Add(record);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return record.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> RecordDayAsync(long companyId, long employeeId, DateOnly date, AttendanceStatus status, TimeOnly? clockIn, TimeOnly? clockOut, int lateMinutes, decimal overtimeHours,
        decimal overtimeMultiplier, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(status) || lateMinutes < 0 || overtimeHours < 0m || (overtimeHours > 0m && overtimeMultiplier < 1m))
        {
            return Error.Validation("Hr.AttendanceInvalid", "A day needs a status, non-negative lateness and overtime, and an overtime multiplier of at least 1.");
        }

        if (status != AttendanceStatus.Present && (overtimeHours > 0m || lateMinutes > 0))
        {
            return Error.Rule("Hr.AttendanceAbsentWork", "An absent or sick day cannot carry lateness or overtime.");
        }

        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId && e.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Hr.EmployeeNotFound", $"Employee {employeeId} not found.");
        }

        if (await _db.AttendanceRecords.AnyAsync(a => a.EmployeeId == employeeId && a.Date == date, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hr.AttendanceExists", $"A day is already recorded for {date:yyyy-MM-dd}.");
        }

        AttendanceRecord record = new(companyId, employeeId, date, status, clockIn, clockOut, lateMinutes, overtimeHours, overtimeHours > 0m ? overtimeMultiplier : 0m);
        _db.AttendanceRecords.Add(record);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return record.Id;
    }

    /// <inheritdoc />
    public async Task<Result> ApproveOvertimeAsync(long companyId, long attendanceId, long approverUserId, CancellationToken ct = default)
    {
        AttendanceRecord? record = await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.Id == attendanceId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (record is null)
        {
            return Result.Failure(Error.NotFound("Hr.AttendanceNotFound", $"Attendance {attendanceId} not found."));
        }

        if (record.OvertimeHours <= 0m)
        {
            return Result.Failure(Error.Rule("Hr.NoOvertime", "The day has no overtime to approve."));
        }

        Employee employee = await _db.Employees.AsNoTracking().SingleAsync(e => e.Id == record.EmployeeId, ct).ConfigureAwait(false);
        if (employee.UserId == approverUserId)
        {
            return Result.Failure(Error.Rule("Hr.SelfApproval", "Employees cannot approve their own overtime."));
        }

        record.ApproveOvertime(approverUserId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
