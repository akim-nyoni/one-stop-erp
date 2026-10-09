using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>Whether leave was taken or paid out.</summary>
public enum LeaveRecordKind
{
    /// <summary>Leave taken as time off.</summary>
    Taken = 1,

    /// <summary>Leave paid out instead of taken.</summary>
    Encashed = 2,
}

/// <summary>A dated leave movement (<c>hr.leave_record</c>): time off or an encashment, behind the running balance.</summary>
public sealed class LeaveRecord : Entity, ICompanyScoped
{
    private LeaveRecord()
    {
        LeaveType = string.Empty;
    }

    /// <summary>Records leave.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="leaveType">The leave type.</param>
    /// <param name="kind">Taken or encashed.</param>
    /// <param name="fromDate">The first day (or the encashment date).</param>
    /// <param name="toDate">The last day.</param>
    /// <param name="days">The days.</param>
    /// <param name="amount">The amount paid, for an encashment.</param>
    public LeaveRecord(long companyId, long employeeId, string leaveType, LeaveRecordKind kind, DateOnly fromDate, DateOnly toDate, decimal days, decimal amount)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        LeaveType = leaveType;
        Kind = kind;
        FromDate = fromDate;
        ToDate = toDate;
        Days = days;
        Amount = amount;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>Gets the leave type.</summary>
    public string LeaveType { get; private set; }

    /// <summary>Gets whether it was taken or encashed.</summary>
    public LeaveRecordKind Kind { get; private set; }

    /// <summary>Gets the first day.</summary>
    public DateOnly FromDate { get; private set; }

    /// <summary>Gets the last day.</summary>
    public DateOnly ToDate { get; private set; }

    /// <summary>Gets the days.</summary>
    public decimal Days { get; private set; }

    /// <summary>Gets the amount paid (encashments).</summary>
    public decimal Amount { get; private set; }

    /// <summary>Gets whether the leave was cancelled before it began (its days went back to the balance).</summary>
    public bool IsCancelled { get; private set; }

    /// <summary>Cancels leave not yet begun.</summary>
    public void Cancel()
    {
        IsCancelled = true;
        Days = 0m;
    }

    /// <summary>Cuts leave short when the employee is recalled: it ends the day before they return.</summary>
    /// <param name="returnedOn">The day they are back at work.</param>
    /// <param name="daysBack">The days returned to the balance.</param>
    public void Curtail(DateOnly returnedOn, decimal daysBack)
    {
        ToDate = returnedOn.AddDays(-1);
        Days -= daysBack;
    }
}

/// <summary>How an employee's day went.</summary>
public enum AttendanceStatus
{
    /// <summary>Worked.</summary>
    Present = 1,

    /// <summary>Did not come in, without leave.</summary>
    Absent = 2,

    /// <summary>Off sick.</summary>
    Sick = 3,
}

/// <summary>An employee's day at work (<c>hr.attendance</c>): clock times, lateness and overtime.</summary>
public sealed class AttendanceRecord : Entity, ICompanyScoped
{
    private AttendanceRecord()
    {
    }

    /// <summary>Records a day.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="date">The day.</param>
    /// <param name="status">How the day went.</param>
    /// <param name="clockIn">When they clocked in.</param>
    /// <param name="clockOut">When they clocked out.</param>
    /// <param name="lateMinutes">Minutes late.</param>
    /// <param name="overtimeHours">Overtime worked.</param>
    /// <param name="overtimeMultiplier">The overtime rate multiplier (1.5, 2…).</param>
    public AttendanceRecord(long companyId, long employeeId, DateOnly date, AttendanceStatus status, TimeOnly? clockIn, TimeOnly? clockOut, int lateMinutes, decimal overtimeHours, decimal overtimeMultiplier)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        Date = date;
        Status = status;
        ClockIn = clockIn;
        ClockOut = clockOut;
        LateMinutes = lateMinutes;
        OvertimeHours = overtimeHours;
        OvertimeMultiplier = overtimeMultiplier;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>Gets the day.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets how the day went.</summary>
    public AttendanceStatus Status { get; private set; }

    /// <summary>Gets the clock-in time.</summary>
    public TimeOnly? ClockIn { get; private set; }

    /// <summary>Gets the clock-out time.</summary>
    public TimeOnly? ClockOut { get; private set; }

    /// <summary>Gets the minutes late.</summary>
    public int LateMinutes { get; private set; }

    /// <summary>Gets the overtime hours.</summary>
    public decimal OvertimeHours { get; private set; }

    /// <summary>Gets the overtime multiplier.</summary>
    public decimal OvertimeMultiplier { get; private set; }

    /// <summary>Gets who approved the overtime.</summary>
    public long? OvertimeApprovedByUserId { get; private set; }

    /// <summary>Approves the day's overtime.</summary>
    /// <param name="userId">The approver.</param>
    public void ApproveOvertime(long userId) => OvertimeApprovedByUserId = userId;
}
