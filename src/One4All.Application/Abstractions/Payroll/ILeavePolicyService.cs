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
    IReadOnlyList<(int FromYears, decimal AnnualDays)> Bands);

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
}
