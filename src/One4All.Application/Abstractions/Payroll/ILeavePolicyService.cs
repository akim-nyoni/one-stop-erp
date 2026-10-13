using One4All.Core.Common;

namespace One4All.Application.Abstractions.Payroll;

/// <summary>A leave policy to save.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="LeaveType">The leave type.</param>
/// <param name="AnnualDays">Yearly days before service bands.</param>
/// <param name="AccruesMonthly">Earned monthly (else granted at the leave-year start).</param>
/// <param name="LeaveYearStartMonth">The month the leave year starts.</param>
/// <param name="CarryOverCapDays">The most carried into a new leave year (null: no cap).</param>
/// <param name="ProRataJoiners">Joiners pro rata.</param>
/// <param name="Bands">Service bands: completed years → yearly days.</param>
public sealed record LeavePolicyRequest(long CompanyId, string LeaveType, decimal AnnualDays, bool AccruesMonthly, int LeaveYearStartMonth, decimal? CarryOverCapDays, bool ProRataJoiners,
    IReadOnlyList<(int FromYears, decimal AnnualDays)> Bands, decimal? MaxAccumulationDays = null, int? CarryOverExpiryMonths = null, decimal NegativeBalanceDays = 0m,
    IReadOnlyList<(int FromDay, decimal PayPercent)>? SickTiers = null)
{
    /// <summary>Gets the yearly days by grade ("B1=24; B2=28").</summary>
    public string? GradeDays { get; init; }

    /// <summary>Gets the most people of one department who may be away at once.</summary>
    public int? MaxAwayPerDepartment { get; init; }
}

/// <summary>One employee's sick pay for a period under the tiers.</summary>
/// <param name="EmployeeId">The employee.</param>
/// <param name="SickDays">Sick days starting in the period.</param>
/// <param name="UnpaidDays">Their unpaid equivalent (a day at 50% counts a half).</param>
/// <param name="Deduction">The pay deduction (unpaid days × basic ÷ working days).</param>
public sealed record SickPayLine(long EmployeeId, decimal SickDays, decimal UnpaidDays, decimal Deduction);

/// <summary>Leave pay paid ahead of an employee's leave, and its recovery.</summary>
/// <param name="EmployeeId">The employee.</param>
/// <param name="LeaveDays">The leave days starting in the window.</param>
/// <param name="Amount">The leave pay paid now and recovered in the recovery period.</param>
public sealed record LeaveAdvanceLine(long EmployeeId, decimal LeaveDays, decimal Amount);

/// <summary>A month's leave policy run.</summary>
/// <param name="Period">The month (yyyyMM).</param>
/// <param name="Accrued">Balances accrued or granted.</param>
/// <param name="DaysAccrued">Days accrued in all.</param>
/// <param name="DaysForfeited">Days forfeited to carry-over caps as the leave year turned.</param>
/// <param name="AlreadyRun">Balances the month had already been run for.</param>
public sealed record LeaveAccrualRun(int Period, int Accrued, decimal DaysAccrued, decimal DaysForfeited, int AlreadyRun);

/// <summary>
/// Leave policies (brief §4.15): yearly entitlement with service bands, monthly accrual or a yearly grant, pro rata joiners and leavers, and
/// a carry-over cap applied as the leave year turns. The monthly run is idempotent per employee and leave type, and is also the
/// <c>payroll.leave-accrual</c> scheduled task.
/// </summary>
public interface ILeavePolicyService
{
    /// <summary>Creates or updates the policy for a leave type.</summary>
    Task<Result> SavePolicyAsync(LeavePolicyRequest request, CancellationToken ct = default);

    /// <summary>Runs the policies for a month: carry-over caps first when a leave year starts, then that month's accrual or grant.</summary>
    Task<Result<LeaveAccrualRun>> RunMonthAsync(long companyId, DateOnly month, CancellationToken ct = default);

    /// <summary>
    /// Applies a leave type's sick pay tiers to a pay period: each sick day taken from the period's start to its end is placed in the leave year
    /// (days already taken that year count first), priced at its tier's share of pay, and the unpaid part is set as a deduction variable for the period.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="leaveType">The sick leave type (its policy carries the tiers).</param>
    /// <param name="periodLabel">The pay period the deduction goes into.</param>
    /// <param name="fromDate">The period's first day.</param>
    /// <param name="toDate">The period's last day.</param>
    /// <param name="deductionElementCode">The deduction pay element.</param>
    /// <param name="workingDaysPerMonth">Working days a month (a day's pay is basic ÷ this).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The employees with sick days in the period.</returns>
    Task<Result<IReadOnlyList<SickPayLine>>> ApplySickPayAsync(long companyId, string leaveType, string periodLabel, DateOnly fromDate, DateOnly toDate, string deductionElementCode,
        decimal workingDaysPerMonth, CancellationToken ct = default);

    /// <summary>
    /// Pays leave pay in advance (brief §4.15.4): for leave of a type starting between two dates, the leave days' pay (basic ÷ working days a month,
    /// per day) goes into this pay period as the advance element and comes back in the recovery period as the recovery deduction, when the
    /// leave days are paid in the ordinary way. Rerunning replaces the earlier figures.
    /// </summary>
    Task<Result<IReadOnlyList<LeaveAdvanceLine>>> AdvanceLeavePayAsync(long companyId, string leaveType, DateOnly fromDate, DateOnly toDate, string periodLabel, string recoveryPeriodLabel,
        string advanceElementCode, string recoveryElementCode, decimal workingDaysPerMonth, CancellationToken ct = default);

    /// <summary>
    /// Credits time off in lieu for overtime (brief §4.15 TOIL): the hours worked, at the ratio, over the hours in a day, added to the employee's balance
    /// of the leave type (created when they have none).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="employeeNumber">The employee.</param>
    /// <param name="overtimeHours">The overtime worked.</param>
    /// <param name="hoursPerDay">The hours in a working day.</param>
    /// <param name="ratio">Hours off per hour worked.</param>
    /// <param name="leaveType">The leave type the time is credited to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The days credited, or why not.</returns>
    Task<Result<decimal>> CreditTimeOffAsync(long companyId, string employeeNumber, decimal overtimeHours, decimal hoursPerDay, decimal ratio = 1m, string leaveType = "TOIL", CancellationToken ct = default);
}
