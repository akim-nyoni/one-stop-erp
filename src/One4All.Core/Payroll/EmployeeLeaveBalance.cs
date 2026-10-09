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

    /// <summary>Gives back leave days taken (cancelled or curtailed leave).</summary>
    /// <param name="days">The days given back.</param>
    public void Restore(decimal days) => TakenDays = Math.Max(0m, decimal.Round(TakenDays - days, 4, MidpointRounding.ToEven));

    /// <summary>Records leave taken.</summary>
    /// <param name="days">The days taken.</param>
    public void Take(decimal days)
    {
        TakenDays = decimal.Round(TakenDays + days, 4, MidpointRounding.ToEven);
        CarriedOverDays = Math.Max(0m, CarriedOverDays - days);
    }

    /// <summary>Days carried into this leave year still untaken (taken first; they lapse at <see cref="CarryExpiresOn"/>).</summary>
    public decimal CarriedOverDays { get; private set; }

    /// <summary>When the carried-over days lapse; null: they do not.</summary>
    public DateOnly? CarryExpiresOn { get; private set; }

    /// <summary>Marks what the year turned with as carried over, to lapse on a date.</summary>
    /// <param name="expiresOn">When untaken carried days lapse.</param>
    public void StartCarry(DateOnly expiresOn)
    {
        CarriedOverDays = Math.Max(0m, BalanceDays);
        CarryExpiresOn = CarriedOverDays > 0m ? expiresOn : null;
    }

    /// <summary>Lapses the carried-over days still untaken.</summary>
    /// <returns>The days forfeited.</returns>
    public decimal ExpireCarried()
    {
        decimal lapsed = Math.Min(CarriedOverDays, Math.Max(0m, BalanceDays));
        AccruedDays = decimal.Round(AccruedDays - lapsed, 4, MidpointRounding.ToEven);
        ForfeitedDays = decimal.Round(ForfeitedDays + lapsed, 4, MidpointRounding.ToEven);
        CarriedOverDays = 0m;
        CarryExpiresOn = null;
        return lapsed;
    }

    /// <summary>The last month (yyyyMM) the policy run accrued or granted leave for.</summary>
    public int? LastAccruedPeriod { get; private set; }

    /// <summary>The last leave year (its start year) whose carry-over cap was applied.</summary>
    public int? LastYearEndApplied { get; private set; }

    /// <summary>Days lost to the carry-over cap over time.</summary>
    public decimal ForfeitedDays { get; private set; }

    /// <summary>Records a policy run's accrual for a month.</summary>
    /// <param name="period">The month (yyyyMM).</param>
    /// <param name="days">The days accrued (may be nil).</param>
    /// <param name="ceiling">The most the balance may hold (null: no ceiling); what would go above it is forfeited.</param>
    /// <returns>The days forfeited to the ceiling.</returns>
    public decimal AccrueFor(int period, decimal days, decimal? ceiling = null)
    {
        decimal lost = ceiling is decimal max ? Math.Clamp(BalanceDays + days - max, 0m, days) : 0m;
        Accrue(days - lost);
        ForfeitedDays = decimal.Round(ForfeitedDays + lost, 4, MidpointRounding.ToEven);
        LastAccruedPeriod = period;
        return lost;
    }

    /// <summary>Applies the carry-over cap at a leave-year start: anything above it is forfeited.</summary>
    /// <param name="leaveYear">The leave year starting.</param>
    /// <param name="cap">The most that may be carried (null: all of it).</param>
    /// <returns>The days forfeited.</returns>
    public decimal ApplyCarryOverCap(int leaveYear, decimal? cap)
    {
        LastYearEndApplied = leaveYear;
        decimal excess = cap is decimal c ? BalanceDays - c : 0m;
        if (excess <= 0m)
        {
            return 0m;
        }

        AccruedDays = decimal.Round(AccruedDays - excess, 4, MidpointRounding.ToEven);
        ForfeitedDays = decimal.Round(ForfeitedDays + excess, 4, MidpointRounding.ToEven);
        return excess;
    }
}
