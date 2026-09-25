using One4All.Core.Common;

namespace One4All.Core.Payroll;

/// <summary>
/// An employee's leave balance for one leave type (<c>pay.leave_balance</c>): days accrued (entitlement
/// earned) less days taken. Operational HR tracking (no GL by itself; the leave provision is separate).
/// </summary>
public sealed class EmployeeLeaveBalance : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private EmployeeLeaveBalance() => LeaveType = string.Empty;

    /// <summary>Creates a leave balance.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeId">The employee.</param>
    /// <param name="leaveType">The leave type (e.g. ANNUAL, SICK), normalised to upper case.</param>
    public EmployeeLeaveBalance(long companyId, long employeeId, string leaveType)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        LeaveType = leaveType.Trim().ToUpperInvariant();
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The employee.</summary>
    public long EmployeeId { get; private set; }

    /// <summary>The leave type.</summary>
    public string LeaveType { get; private set; }

    /// <summary>The days accrued (entitlement earned).</summary>
    public decimal AccruedDays { get; private set; }

    /// <summary>The days taken.</summary>
    public decimal TakenDays { get; private set; }

    /// <summary>The available balance (accrued − taken).</summary>
    public decimal BalanceDays => decimal.Round(AccruedDays - TakenDays, 4, MidpointRounding.ToEven);

    /// <summary>Accrues leave days (entitlement earned).</summary>
    /// <param name="days">The days to accrue.</param>
    public void Accrue(decimal days) => AccruedDays = decimal.Round(AccruedDays + days, 4, MidpointRounding.ToEven);

    /// <summary>Records leave taken.</summary>
    /// <param name="days">The days taken.</param>
    public void Take(decimal days) => TakenDays = decimal.Round(TakenDays + days, 4, MidpointRounding.ToEven);
}
