using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <summary>Shift rosters (brief §4.15).</summary>
public sealed class RosterService : IRosterService
{
    private const int MaxDays = 366;
    private readonly One4AllDbContext _db;
    private readonly IPayrollVariableService _variables;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="variables">Payroll variables (shift allowances).</param>
    public RosterService(One4AllDbContext db, IPayrollVariableService variables)
    {
        _db = db;
        _variables = variables;
    }

    /// <inheritdoc />
    public async Task<Result> SaveShiftAsync(long companyId, ShiftRequest shift, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(shift);
        string code = (shift.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0 || code == ShiftPattern.Off || code.Contains(',', StringComparison.Ordinal) || string.IsNullOrWhiteSpace(shift.Name) || shift.Start == shift.End
            || shift.GraceMinutes < 0 || shift.Allowance < 0m)
        {
            return Result.Failure(Error.Validation("Hr.Shift", "A shift needs a code (not OFF, no commas), a name, different start and end times, and no negative grace or allowance."));
        }

        Shift? existing = await _db.Set<Shift>().FirstOrDefaultAsync(s => s.CompanyId == companyId && s.Code == code, ct).ConfigureAwait(false);
        if (existing is null)
        {
            existing = new Shift(companyId, code, shift.Name.Trim(), shift.Start, shift.End);
            _db.Set<Shift>().Add(existing);
        }

        existing.Name = shift.Name.Trim();
        existing.Start = shift.Start;
        existing.End = shift.End;
        existing.GraceMinutes = shift.GraceMinutes;
        existing.Allowance = shift.Allowance;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SavePatternAsync(long companyId, string code, string steps, CancellationToken ct = default)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        List<string> cycle = [.. (steps ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => s.ToUpperInvariant())];
        HashSet<string> shifts = [.. await _db.Set<Shift>().Where(s => s.CompanyId == companyId).Select(s => s.Code).ToListAsync(ct).ConfigureAwait(false)];
        string? unknown = cycle.FirstOrDefault(s => s != ShiftPattern.Off && !shifts.Contains(s));
        if (key.Length == 0 || cycle.Count == 0 || unknown is not null)
        {
            return Result.Failure(Error.Validation("Hr.ShiftPattern", unknown is null ? "A pattern needs a code and at least one step." : $"Step '{unknown}' is not a shift (or OFF)."));
        }

        ShiftPattern? pattern = await _db.Set<ShiftPattern>().FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Code == key, ct).ConfigureAwait(false);
        if (pattern is null)
        {
            _db.Set<ShiftPattern>().Add(new ShiftPattern(companyId, key, string.Join(',', cycle)));
        }
        else
        {
            pattern.Steps = string.Join(',', cycle);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> AssignAsync(long companyId, long employeeId, string patternCode, DateOnly anchor, CancellationToken ct = default)
    {
        string key = (patternCode ?? string.Empty).Trim().ToUpperInvariant();
        ShiftPattern? pattern = await _db.Set<ShiftPattern>().FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Code == key, ct).ConfigureAwait(false);
        if (pattern is null)
        {
            return Result.Failure(Error.NotFound("Hr.ShiftPatternNotFound", $"No shift pattern {key}."));
        }

        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId && e.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Hr.EmployeeNotFound", $"Employee {employeeId} not found."));
        }

        RosterAssignment? assignment = await _db.Set<RosterAssignment>().FirstOrDefaultAsync(a => a.EmployeeId == employeeId, ct).ConfigureAwait(false);
        if (assignment is null)
        {
            _db.Set<RosterAssignment>().Add(new RosterAssignment(companyId, employeeId, pattern.Id, anchor));
        }
        else
        {
            assignment.ShiftPatternId = pattern.Id;
            assignment.Anchor = anchor;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<int>> GenerateAsync(long companyId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            if (to < from || to.DayNumber - from.DayNumber >= MaxDays)
            {
                return Error.Validation("Hr.RosterPeriod", $"Generate a roster for up to {MaxDays} days, ending on or after it starts.");
            }

            List<RosterAssignment> assignments = await _db.Set<RosterAssignment>().Where(a => a.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
            Dictionary<long, ShiftPattern> patterns = await _db.Set<ShiftPattern>().Where(p => p.CompanyId == companyId).ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
            Dictionary<string, long> shifts = await _db.Set<Shift>().Where(s => s.CompanyId == companyId).ToDictionaryAsync(s => s.Code, s => s.Id, ct).ConfigureAwait(false);
            List<long> ids = [.. assignments.Select(a => a.EmployeeId)];
            Dictionary<(long, DateOnly), RosterEntry> existing = (await _db.Set<RosterEntry>().Where(r => ids.Contains(r.EmployeeId) && r.Date >= @from && r.Date <= to).ToListAsync(ct).ConfigureAwait(false))
                .ToDictionary(r => (r.EmployeeId, r.Date));
            int written = 0;
            foreach (RosterAssignment assignment in assignments)
            {
                ShiftPattern pattern = patterns[assignment.ShiftPatternId];
                for (DateOnly day = from; day <= to; day = day.AddDays(1))
                {
                    if (day < assignment.Anchor)
                    {
                        continue;
                    }

                    string step = pattern.StepOn(assignment.Anchor, day);
                    long? shift = step == ShiftPattern.Off ? null : shifts[step];
                    if (existing.TryGetValue((assignment.EmployeeId, day), out RosterEntry? entry))
                    {
                        if (entry.IsManual)
                        {
                            continue;
                        }

                        entry.Set(shift, false);
                    }
                    else
                    {
                        _db.Set<RosterEntry>().Add(new RosterEntry(companyId, assignment.EmployeeId, day, shift, false));
                    }

                    written++;
                }
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<int>.Success(written);
        }, ct);

    /// <inheritdoc />
    public async Task<Result> SetDayAsync(long companyId, long employeeId, DateOnly date, string shiftCode, CancellationToken ct = default)
    {
        string code = (shiftCode ?? string.Empty).Trim().ToUpperInvariant();
        long? shift = null;
        if (code != ShiftPattern.Off)
        {
            shift = await _db.Set<Shift>().Where(s => s.CompanyId == companyId && s.Code == code).Select(s => (long?)s.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (shift is null)
            {
                return Result.Failure(Error.NotFound("Hr.ShiftNotFound", $"No shift {code} (or OFF)."));
            }
        }

        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId && e.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Hr.EmployeeNotFound", $"Employee {employeeId} not found."));
        }

        RosterEntry? entry = await _db.Set<RosterEntry>().FirstOrDefaultAsync(r => r.EmployeeId == employeeId && r.Date == date, ct).ConfigureAwait(false);
        if (entry is null)
        {
            _db.Set<RosterEntry>().Add(new RosterEntry(companyId, employeeId, date, shift, true));
        }
        else
        {
            entry.Set(shift, true);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RosterDay>> RosterAsync(long companyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var days = await (from r in _db.Set<RosterEntry>().AsNoTracking()
                          join e in _db.Employees.AsNoTracking() on r.EmployeeId equals e.Id
                          join s in _db.Set<Shift>().AsNoTracking() on r.ShiftId equals s.Id into shifts
                          from s in shifts.DefaultIfEmpty()
                          where r.CompanyId == companyId && r.Date >= @from && r.Date <= to
                          select new { r.EmployeeId, e.EmployeeNumber, r.Date, Shift = s == null ? ShiftPattern.Off : s.Code, r.IsManual }).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<(long, DateOnly), AttendanceStatus> attended = (await _db.AttendanceRecords.AsNoTracking().Where(a => a.CompanyId == companyId && a.Date >= from && a.Date <= to)
            .Select(a => new { a.EmployeeId, a.Date, a.Status }).ToListAsync(ct).ConfigureAwait(false)).ToDictionary(a => (a.EmployeeId, a.Date), a => a.Status);
        return [.. days.OrderBy(d => d.EmployeeNumber, StringComparer.Ordinal).ThenBy(d => d.Date)
            .Select(d => new RosterDay(d.EmployeeNumber, d.Date, d.Shift, d.IsManual, attended.TryGetValue((d.EmployeeId, d.Date), out AttendanceStatus status) ? status.ToString() : null))];
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<ShiftAllowanceLine>>> ApplyShiftAllowancesAsync(long companyId, string periodLabel, DateOnly from, DateOnly to, string elementCode, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            if (to < from)
            {
                return Error.Validation("Hr.RosterPeriod", "The period ends before it starts.");
            }

            var worked = await (from r in _db.Set<RosterEntry>().AsNoTracking()
                                join s in _db.Set<Shift>().AsNoTracking() on r.ShiftId equals s.Id
                                join a in _db.AttendanceRecords.AsNoTracking() on new { r.EmployeeId, r.Date } equals new { a.EmployeeId, a.Date }
                                where r.CompanyId == companyId && r.Date >= @from && r.Date <= to && s.Allowance > 0m && a.Status == AttendanceStatus.Present
                                select new { r.EmployeeId, s.Allowance }).ToListAsync(ct).ConfigureAwait(false);
            List<ShiftAllowanceLine> lines = [];
            foreach (var employee in worked.GroupBy(w => w.EmployeeId).OrderBy(g => g.Key))
            {
                decimal amount = employee.Sum(w => w.Allowance);
                Result<long> set = await _variables.SetAsync(companyId, employee.Key, elementCode, periodLabel, null, amount, ct).ConfigureAwait(false);
                if (set.IsFailure)
                {
                    return set.Error;
                }

                lines.Add(new ShiftAllowanceLine(employee.Key, employee.Count(), amount));
            }

            return Result<IReadOnlyList<ShiftAllowanceLine>>.Success(lines);
        }, ct);
}
