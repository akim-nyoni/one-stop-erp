using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Payroll;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <summary>Leave policies and their monthly run (brief §4.15).</summary>
public sealed class LeavePolicyService : ILeavePolicyService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public LeavePolicyService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SavePolicyAsync(LeavePolicyRequest request, CancellationToken ct = default)
    {
        string type = (request.LeaveType ?? string.Empty).Trim().ToUpperInvariant();
        if (type.Length == 0 || request.AnnualDays < 0m || request.LeaveYearStartMonth is < 1 or > 12 || request.CarryOverCapDays < 0m
            || request.Bands.Any(b => b.FromYears < 1 || b.AnnualDays < 0m) || request.Bands.Select(b => b.FromYears).Distinct().Count() != request.Bands.Count)
        {
            return Result.Failure(Error.Validation("Pay.LeavePolicy", "A leave policy needs a leave type, days of nil or more, a start month 1–12, no negative carry-over cap and distinct service bands from one year."));
        }

        LeavePolicy? policy = await _db.Set<LeavePolicy>().Include(p => p.Bands).FirstOrDefaultAsync(p => p.CompanyId == request.CompanyId && p.LeaveType == type, ct).ConfigureAwait(false);
        if (policy is null)
        {
            policy = new LeavePolicy(request.CompanyId, type, request.AnnualDays);
            _db.Set<LeavePolicy>().Add(policy);
        }

        policy.AnnualDays = request.AnnualDays;
        policy.AccruesMonthly = request.AccruesMonthly;
        policy.LeaveYearStartMonth = request.LeaveYearStartMonth;
        policy.CarryOverCapDays = request.CarryOverCapDays;
        policy.ProRataJoiners = request.ProRataJoiners;
        policy.SetBands(request.Bands);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<LeaveAccrualRun>> RunMonthAsync(long companyId, DateOnly month, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            int period = (month.Year * 100) + month.Month;
            DateOnly first = new(month.Year, month.Month, 1);
            List<LeavePolicy> policies = await _db.Set<LeavePolicy>().Include(p => p.Bands).Where(p => p.CompanyId == companyId && p.IsActive).ToListAsync(ct).ConfigureAwait(false);
            if (policies.Count == 0)
            {
                return Error.Rule("Pay.NoLeavePolicy", "No leave policy is set up.");
            }

            List<Employee> employees = await _db.Employees.Where(e => e.CompanyId == companyId && (e.Status != EmployeeStatus.Terminated || e.TerminationDate >= first))
                .ToListAsync(ct).ConfigureAwait(false);
            List<long> ids = [.. employees.Select(e => e.Id)];
            List<string> types = [.. policies.Select(p => p.LeaveType)];
            Dictionary<(long, string), EmployeeLeaveBalance> balances = (await _db.Set<EmployeeLeaveBalance>()
                    .Where(b => b.CompanyId == companyId && ids.Contains(b.EmployeeId) && types.Contains(b.LeaveType)).ToListAsync(ct).ConfigureAwait(false))
                .ToDictionary(b => (b.EmployeeId, b.LeaveType));

            int accrued = 0, alreadyRun = 0;
            decimal days = 0m, forfeited = 0m;
            foreach (LeavePolicy policy in policies)
            {
                foreach (Employee employee in employees)
                {
                    balances.TryGetValue((employee.Id, policy.LeaveType), out EmployeeLeaveBalance? balance);

                    // The leave year turns: only the capped balance carries on.
                    if (balance is not null && month.Month == policy.LeaveYearStartMonth && policy.CarryOverCapDays is decimal cap && balance.LastYearEndApplied != month.Year
                        && (balance.LastAccruedPeriod is null || balance.LastAccruedPeriod < period))
                    {
                        forfeited += balance.ApplyCarryOverCap(month.Year, cap);
                    }

                    decimal earned = LeavePolicyRules.DaysForMonth(policy, employee.EngagementDate, employee.TerminationDate, month);
                    if (balance is null)
                    {
                        if (earned == 0m)
                        {
                            continue;
                        }

                        balance = new EmployeeLeaveBalance(companyId, employee.Id, policy.LeaveType);
                        _db.Set<EmployeeLeaveBalance>().Add(balance);
                        balances[(employee.Id, policy.LeaveType)] = balance;
                    }

                    if (balance.LastAccruedPeriod >= period)
                    {
                        alreadyRun++;
                        continue;
                    }

                    balance.AccrueFor(period, earned);
                    if (earned > 0m)
                    {
                        accrued++;
                        days += earned;
                    }
                }
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<LeaveAccrualRun>.Success(new LeaveAccrualRun(period, accrued, days, forfeited, alreadyRun));
        }, ct);
}

/// <summary>Scheduled task: runs the leave policies for the month just ended.</summary>
public sealed class LeaveAccrualTask : Application.Abstractions.Scheduling.IScheduledTask
{
    private readonly ILeavePolicyService _policies;

    /// <summary>Creates the task.</summary>
    /// <param name="policies">The leave policies.</param>
    public LeaveAccrualTask(ILeavePolicyService policies) => _policies = policies;

    /// <inheritdoc />
    public string Key => "payroll.leave-accrual";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        DateOnly lastMonth = DateOnly.FromDateTime(runAt.UtcDateTime).AddMonths(-1);
        Result<LeaveAccrualRun> run = await _policies.RunMonthAsync(companyId, lastMonth, ct).ConfigureAwait(false);
        return run.IsFailure ? run.Error
            : $"Leave {run.Value.Period}: {run.Value.Accrued} balance(s) earned {run.Value.DaysAccrued:0.##} day(s), {run.Value.DaysForfeited:0.##} forfeited, {run.Value.AlreadyRun} already run.";
    }
}
