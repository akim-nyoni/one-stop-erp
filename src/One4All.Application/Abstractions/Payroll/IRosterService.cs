using One4All.Core.Common;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>A shift's definition.</summary>
/// <param name="Code">The code used in patterns.</param>
/// <param name="Name">The name.</param>
/// <param name="Start">When it starts.</param>
/// <param name="End">When it ends (earlier than the start: the next day).</param>
/// <param name="GraceMinutes">Minutes before arriving counts as late.</param>
/// <param name="Allowance">The allowance per shift worked.</param>
public sealed record ShiftRequest(string Code, string Name, TimeOnly Start, TimeOnly End, int GraceMinutes = 0, decimal Allowance = 0m);

/// <summary>One rostered day.</summary>
/// <param name="EmployeeNumber">The employee.</param>
/// <param name="Date">The day.</param>
/// <param name="Shift">The shift code, or OFF.</param>
/// <param name="IsManual">Changed by hand.</param>
/// <param name="Attendance">What attendance recorded that day (null: nothing yet).</param>
public sealed record RosterDay(string EmployeeNumber, DateOnly Date, string Shift, bool IsManual, string? Attendance);

/// <summary>One employee's shift allowances for a period.</summary>
/// <param name="EmployeeId">The employee.</param>
/// <param name="ShiftsWorked">Rostered shifts with an allowance worked.</param>
/// <param name="Amount">The allowance set for the pay run.</param>
public sealed record ShiftAllowanceLine(long EmployeeId, int ShiftsWorked, decimal Amount);

/// <summary>
/// Shift rosters (brief §4.15 attendance/time): shifts and rotating patterns, the roster generated from them (hand changes kept), lateness measured
/// against the rostered start, and shift allowances paid for rostered shifts actually worked.
/// </summary>
public interface IRosterService
{
    /// <summary>Saves a shift.</summary>
    Task<Result> SaveShiftAsync(long companyId, ShiftRequest shift, CancellationToken ct = default);

    /// <summary>Saves a pattern ("D,D,N,N,OFF,OFF"): every step a known shift or OFF.</summary>
    Task<Result> SavePatternAsync(long companyId, string code, string steps, CancellationToken ct = default);

    /// <summary>Puts an employee on a pattern from an anchor date (the cycle's first day).</summary>
    Task<Result> AssignAsync(long companyId, long employeeId, string patternCode, DateOnly anchor, CancellationToken ct = default);

    /// <summary>Generates the roster for a period from the patterns, leaving days changed by hand; returns the days written.</summary>
    Task<Result<int>> GenerateAsync(long companyId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Changes one employee's day by hand (a swap or cover): a shift code, or OFF.</summary>
    Task<Result> SetDayAsync(long companyId, long employeeId, DateOnly date, string shiftCode, CancellationToken ct = default);

    /// <summary>The roster for a period beside what attendance recorded.</summary>
    Task<IReadOnlyList<RosterDay>> RosterAsync(long companyId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Sets each employee's shift allowances for a pay period: rostered shifts with an allowance on which they were present.</summary>
    Task<Result<IReadOnlyList<ShiftAllowanceLine>>> ApplyShiftAllowancesAsync(long companyId, string periodLabel, DateOnly from, DateOnly to, string elementCode, CancellationToken ct = default);
}
