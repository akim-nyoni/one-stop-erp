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

        if (await _db.LeaveRecords.AnyAsync(l => l.EmployeeId == employeeId && l.Kind == LeaveRecordKind.Taken && !l.IsCancelled && l.FromDate <= toDate && l.ToDate >= fromDate, ct).ConfigureAwait(false))
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
    public Task<Result<decimal>> CancelLeaveAsync(long companyId, long leaveRecordId, DateOnly? returnedOn, decimal? daysBack = null, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            LeaveRecord? record = await _db.LeaveRecords.FirstOrDefaultAsync(l => l.Id == leaveRecordId && l.CompanyId == companyId && l.Kind == LeaveRecordKind.Taken && !l.IsCancelled, ct)
                .ConfigureAwait(false);
            if (record is null)
            {
                return Error.NotFound("Hr.LeaveNotFound", $"No leave {leaveRecordId} to cancel.");
            }

            decimal back;
            if (returnedOn is not DateOnly back0 || back0 <= record.FromDate)
            {
                back = record.Days;
                record.Cancel();
            }
            else if (back0 > record.ToDate)
            {
                return Error.Rule("Hr.LeaveOver", $"The leave ended on {record.ToDate:yyyy-MM-dd}; there is nothing to give back.");
            }
            else
            {
                decimal calendar = record.ToDate.DayNumber - record.FromDate.DayNumber + 1;
                back = daysBack ?? decimal.Round(record.Days * (record.ToDate.DayNumber - back0.DayNumber + 1) / calendar, 2, MidpointRounding.AwayFromZero);
                if (back < 0m || back > record.Days)
                {
                    return Error.Validation("Hr.LeaveDaysBack", $"Between 0 and {record.Days} days can be given back.");
                }

                record.Curtail(back0, back);
            }

            EmployeeLeaveBalance? balance = await _db.Set<EmployeeLeaveBalance>().FirstOrDefaultAsync(b => b.EmployeeId == record.EmployeeId && b.LeaveType == record.LeaveType, ct)
                .ConfigureAwait(false);
            balance?.Restore(back);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<decimal>.Success(back);
        }, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeaveCalendarEntry>> LeaveCalendarAsync(long companyId, DateOnly from, DateOnly to, string? department = null, CancellationToken ct = default)
    {
        string? only = string.IsNullOrWhiteSpace(department) ? null : department.Trim();
        DateOnly first = from, last = to;
        return await (
                from l in _db.LeaveRecords.AsNoTracking()
                join e in _db.Employees.AsNoTracking() on l.EmployeeId equals e.Id
                where l.CompanyId == companyId && l.Kind == LeaveRecordKind.Taken && !l.IsCancelled && l.FromDate <= last && l.ToDate >= first && (only == null || e.Department == only)
                orderby l.FromDate, e.EmployeeNumber
                select new LeaveCalendarEntry(l.Id, e.EmployeeNumber, e.FullName, e.Department, l.LeaveType, l.FromDate, l.ToDate, l.Days))
            .ToListAsync(ct).ConfigureAwait(false);
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

        if (taken.Value < 0m)
        {
            return Error.Rule("Hr.EncashAdvance", "Only leave already earned can be paid out, not leave taken in advance.");
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

        // Lateness not keyed in is measured against the rostered shift's start (beyond its grace).
        if (status == AttendanceStatus.Present && lateMinutes == 0 && clockIn is TimeOnly arrived)
        {
            Shift? rostered = await (from r in _db.Set<RosterEntry>()
                                     join s in _db.Set<Shift>() on r.ShiftId equals s.Id
                                     where r.EmployeeId == employeeId && r.Date == date
                                     select s).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            lateMinutes = rostered?.MinutesLate(arrived) ?? 0;
        }

        AttendanceRecord record = new(companyId, employeeId, date, status, clockIn, clockOut, lateMinutes, overtimeHours, overtimeHours > 0m ? overtimeMultiplier : 0m);
        _db.AttendanceRecords.Add(record);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return record.Id;
    }

    /// <inheritdoc />
    public Task<Result<ClockImport>> ImportClockAsync(long companyId, string export, decimal overtimeMultiplier = 1.5m, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            if (overtimeMultiplier < 1m)
            {
                return Error.Validation("Hr.OvertimeRate", "The overtime rate must be at least 1.");
            }

            IReadOnlyList<Application.Payroll.ClockDay> days = Application.Payroll.ClockPunches.Days(export, out IReadOnlyList<string> problems);
            if (days.Count == 0)
            {
                return Error.Validation("Hr.ClockEmpty", problems.Count == 0 ? "The file holds no punches." : $"No punches could be read. {problems[0]}");
            }

            List<string> badges = [.. days.Select(d => d.EmployeeNumber).Distinct()];
            Dictionary<string, long> employees = await _db.Employees.AsNoTracking().Where(e => e.CompanyId == companyId && badges.Contains(e.EmployeeNumber))
                .ToDictionaryAsync(e => e.EmployeeNumber, e => e.Id, StringComparer.OrdinalIgnoreCase, ct).ConfigureAwait(false);
            List<long> ids = [.. employees.Values];
            DateOnly first = days.Min(d => d.Date), last = days.Max(d => d.Date);
            HashSet<(long, DateOnly)> recorded = [.. (await _db.AttendanceRecords.AsNoTracking().Where(a => ids.Contains(a.EmployeeId) && a.Date >= first && a.Date <= last)
                .Select(a => new { a.EmployeeId, a.Date }).ToListAsync(ct).ConfigureAwait(false)).Select(a => (a.EmployeeId, a.Date))];
            var shifts = await (from r in _db.Set<RosterEntry>()
                                join s in _db.Set<Shift>() on r.ShiftId equals s.Id
                                where ids.Contains(r.EmployeeId) && r.Date >= first && r.Date <= last
                                select new { r.EmployeeId, r.Date, s.Start, s.End }).ToListAsync(ct).ConfigureAwait(false);

            int done = 0, skipped = 0;
            foreach (Application.Payroll.ClockDay day in days.Where(d => employees.ContainsKey(d.EmployeeNumber)))
            {
                long employee = employees[day.EmployeeNumber];
                if (recorded.Contains((employee, day.Date)))
                {
                    skipped++;
                    continue;
                }

                var shift = shifts.FirstOrDefault(s => s.EmployeeId == employee && s.Date == day.Date);
                decimal overtime = Application.Payroll.ClockPunches.Overtime(day, shift?.Start, shift?.End);
                Result<long> saved = await RecordDayAsync(companyId, employee, day.Date, AttendanceStatus.Present, day.ClockIn, day.ClockOut, 0, overtime, overtime > 0m ? overtimeMultiplier : 1m, ct)
                    .ConfigureAwait(false);
                if (saved.IsFailure)
                {
                    return saved.Error;
                }

                done++;
            }

            return Result<ClockImport>.Success(new ClockImport(done, skipped, [.. badges.Where(b => !employees.ContainsKey(b))], problems));
        }, ct);

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
