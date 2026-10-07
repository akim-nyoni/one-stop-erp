using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>A shift (<c>hr.shift</c>, brief §4.15 shift rosters): its times (a night shift may end the next day), lateness grace and the allowance paid per shift worked.</summary>
public sealed class Shift : Entity, ICompanyScoped
{
    private Shift()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Defines a shift.</summary>
    public Shift(long companyId, string code, string name, TimeOnly start, TimeOnly end)
    {
        CompanyId = companyId;
        Code = code;
        Name = name;
        Start = start;
        End = end;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The code used in patterns (e.g. D, N).</summary>
    public string Code { get; private set; }

    /// <summary>The name.</summary>
    public string Name { get; set; }

    /// <summary>When it starts.</summary>
    public TimeOnly Start { get; set; }

    /// <summary>When it ends (earlier than the start: the next day).</summary>
    public TimeOnly End { get; set; }

    /// <summary>Minutes after the start before arriving counts as late.</summary>
    public int GraceMinutes { get; set; }

    /// <summary>The allowance paid for each such shift worked (0: none).</summary>
    public decimal Allowance { get; set; }

    /// <summary>How late a clock-in is against the start (beyond the grace); arriving early is not late.</summary>
    /// <param name="clockIn">The clock-in time.</param>
    /// <returns>Minutes late.</returns>
    public int MinutesLate(TimeOnly clockIn)
    {
        TimeSpan after = clockIn - Start;           // wraps within the day: 00:10 against a 22:00 start is 2h10 late
        int minutes = after.TotalHours > 12 ? 0 : (int)after.TotalMinutes;
        return minutes > GraceMinutes ? minutes : 0;
    }
}

/// <summary>A rotating shift pattern (<c>hr.shift_pattern</c>): a cycle of shift codes and OFF days, e.g. "D,D,N,N,OFF,OFF".</summary>
public sealed class ShiftPattern : Entity, ICompanyScoped
{
    /// <summary>The code for a day off in a pattern.</summary>
    public const string Off = "OFF";

    private ShiftPattern()
    {
        Code = string.Empty;
        Steps = string.Empty;
    }

    /// <summary>Defines a pattern.</summary>
    public ShiftPattern(long companyId, string code, string steps)
    {
        CompanyId = companyId;
        Code = code;
        Steps = steps;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The pattern code.</summary>
    public string Code { get; private set; }

    /// <summary>The cycle, comma-separated shift codes and OFF.</summary>
    public string Steps { get; set; }

    /// <summary>The cycle as a list.</summary>
    public IReadOnlyList<string> Cycle => Steps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The step that falls on a date, counting from the anchor (the cycle's first day).</summary>
    /// <param name="anchor">The day the cycle starts for the employee.</param>
    /// <param name="date">The date.</param>
    /// <returns>A shift code or OFF.</returns>
    public string StepOn(DateOnly anchor, DateOnly date)
    {
        IReadOnlyList<string> cycle = Cycle;
        int day = ((date.DayNumber - anchor.DayNumber) % cycle.Count + cycle.Count) % cycle.Count;
        return cycle[day];
    }
}

/// <summary>An employee on a rotating pattern from an anchor date (<c>hr.roster_assignment</c>).</summary>
public sealed class RosterAssignment : Entity, ICompanyScoped
{
    private RosterAssignment()
    {
    }

    /// <summary>Puts an employee on a pattern.</summary>
    public RosterAssignment(long companyId, long employeeId, long shiftPatternId, DateOnly anchor)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        ShiftPatternId = shiftPatternId;
        Anchor = anchor;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>The pattern.</summary>
    public long ShiftPatternId { get; set; }

    /// <summary>The day the cycle starts for them.</summary>
    public DateOnly Anchor { get; set; }
}

/// <summary>One employee's day on the roster (<c>hr.roster_entry</c>): the shift, or off; a manual change survives regenerating the roster.</summary>
public sealed class RosterEntry : Entity, ICompanyScoped
{
    private RosterEntry()
    {
    }

    /// <summary>Rosters a day.</summary>
    public RosterEntry(long companyId, long employeeId, DateOnly date, long? shiftId, bool isManual)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        Date = date;
        ShiftId = shiftId;
        IsManual = isManual;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>The day.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The shift, or null for a day off.</summary>
    public long? ShiftId { get; private set; }

    /// <summary>Changed by hand (a swap or cover), so regeneration leaves it.</summary>
    public bool IsManual { get; private set; }

    /// <summary>Changes the day.</summary>
    /// <param name="shiftId">The shift, or null for off.</param>
    /// <param name="isManual">Whether by hand.</param>
    public void Set(long? shiftId, bool isManual)
    {
        ShiftId = shiftId;
        IsManual = isManual;
    }
}
