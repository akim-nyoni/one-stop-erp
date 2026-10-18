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
    private readonly IPayrollVariableService _variables;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="variables">Payroll variables (sick pay deductions).</param>
    public LeavePolicyService(One4AllDbContext db, IPayrollVariableService variables)
    {
        _db = db;
        _variables = variables;
    }

    /// <inheritdoc />
    public async Task<Result> SavePolicyAsync(LeavePolicyRequest request, CancellationToken ct = default)
    {
        string type = (request.LeaveType ?? string.Empty).Trim().ToUpperInvariant();
        if (type.Length == 0 || request.AnnualDays < 0m || request.LeaveYearStartMonth is < 1 or > 12 || request.CarryOverCapDays < 0m
            || request.Bands.Any(b => b.FromYears < 1 || b.AnnualDays < 0m) || request.Bands.Select(b => b.FromYears).Distinct().Count() != request.Bands.Count)
        {
            return Result.Failure(Error.Validation("Pay.LeavePolicy", "A leave policy needs a leave type, days of nil or more, a start month 1–12, no negative carry-over cap and distinct service bands from one year."));
        }

        IReadOnlyList<(int FromDay, decimal PayPercent)> tiers = request.SickTiers ?? [];
        if (request.MaxAccumulationDays <= 0m || request.CarryOverExpiryMonths is < 1 or > 12 || request.NegativeBalanceDays < 0m
            || tiers.Any(t => t.FromDay < 1 || t.PayPercent is < 0m or > 100m) || tiers.Select(t => t.FromDay).Distinct().Count() != tiers.Count)
        {
            return Result.Failure(Error.Validation("Pay.LeaveRules", "A balance ceiling must be positive, carried days must lapse 1–12 months into the year, advances cannot be negative, and sick tiers need distinct days from 1 at 0–100% pay."));
        }

        if (request.MaxAwayPerDepartment < 1)
        {
            return Result.Failure(Error.Validation("Pay.LeaveCover", "At least one person of a department must be allowed away at once."));
        }

        foreach (string part in (request.GradeDays ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] bits = part.Split('=', StringSplitOptions.TrimEntries);
            if (bits.Length != 2 || bits[0].Length == 0 || !decimal.TryParse(bits[1], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal gradeDays) || gradeDays < 0m)
            {
                return Result.Failure(Error.Validation("Pay.LeaveGradeDays", $"Write each grade as GRADE=days (\"{part}\")."));
            }
        }

        LeavePolicy? policy = await _db.Set<LeavePolicy>().Include(p => p.Bands).Include(p => p.SickTiers).FirstOrDefaultAsync(p => p.CompanyId == request.CompanyId && p.LeaveType == type, ct).ConfigureAwait(false);
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
        policy.MaxAccumulationDays = request.MaxAccumulationDays;
        policy.CarryOverExpiryMonths = request.CarryOverExpiryMonths;
        policy.NegativeBalanceDays = request.NegativeBalanceDays;
        policy.SetSickTiers(tiers);
        policy.GradeDays = string.IsNullOrWhiteSpace(request.GradeDays) ? null : request.GradeDays.Trim();
        policy.MaxAwayPerDepartment = request.MaxAwayPerDepartment;
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

                    bool notRun = balance is not null && (balance.LastAccruedPeriod is null || balance.LastAccruedPeriod < period);

                    // Carried-over days not taken in time lapse.
                    if (notRun && balance!.CarryExpiresOn is DateOnly lapse && lapse <= first)
                    {
                        forfeited += balance.ExpireCarried();
                    }

                    // The leave year turns: only the capped balance carries on (and starts its clock to lapse, where it does).
                    if (notRun && month.Month == policy.LeaveYearStartMonth && balance!.LastYearEndApplied != month.Year
                        && (policy.CarryOverCapDays is not null || policy.CarryOverExpiryMonths is not null))
                    {
                        forfeited += balance.ApplyCarryOverCap(month.Year, policy.CarryOverCapDays);
                        if (policy.CarryOverExpiryMonths is int months)
                        {
                            balance.StartCarry(first.AddMonths(months));
                        }
                    }

                    decimal earned = LeavePolicyRules.DaysForMonth(policy, employee.EngagementDate, employee.TerminationDate, month, employee.Grade);
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

                    forfeited += balance.AccrueFor(period, earned, policy.MaxAccumulationDays);
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

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AttendancePayLine>>> ApplyAttendanceAsync(long companyId, string periodLabel, DateOnly fromDate, DateOnly toDate, string unpaidElementCode,
        decimal workingDaysPerMonth, IReadOnlyCollection<string>? unpaidLeaveTypes = null, string overtimeElementCode = "OVERTIME", string doubleTimeElementCode = "OVERTIME2",
        CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            if (workingDaysPerMonth <= 0m || toDate < fromDate || string.IsNullOrWhiteSpace(periodLabel) || string.IsNullOrWhiteSpace(unpaidElementCode))
            {
                return Error.Validation("Pay.AttendancePeriod", "Sending attendance to payroll needs a pay period and its dates, the unpaid-days element and working days a month.");
            }

            List<string> unpaidTypes = [.. (unpaidLeaveTypes is { Count: > 0 } ? unpaidLeaveTypes : ["UNPAID"]).Select(t => t.Trim().ToUpperInvariant())];
            List<AttendanceRecord> days = await _db.AttendanceRecords.AsNoTracking().Where(a => a.CompanyId == companyId && a.Date >= fromDate && a.Date <= toDate)
                .ToListAsync(ct).ConfigureAwait(false);
            List<LeaveRecord> leave = await _db.LeaveRecords.AsNoTracking()
                .Where(l => l.CompanyId == companyId && l.Kind == LeaveRecordKind.Taken && !l.IsCancelled && l.FromDate <= toDate && l.ToDate >= fromDate)
                .ToListAsync(ct).ConfigureAwait(false);
            List<long> ids = [.. days.Select(d => d.EmployeeId).Concat(leave.Where(l => unpaidTypes.Contains(l.LeaveType.ToUpperInvariant())).Select(l => l.EmployeeId)).Distinct().Order()];
            Dictionary<long, decimal> basic = await _db.Employees.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.BasicSalary, ct).ConfigureAwait(false);

            List<AttendancePayLine> lines = [];
            foreach (long id in ids)
            {
                List<AttendanceRecord> mine = [.. days.Where(d => d.EmployeeId == id)];
                decimal overtime = mine.Where(d => d.OvertimeApprovedByUserId != null && d.OvertimeMultiplier < 2m).Sum(d => d.OvertimeHours);
                decimal doubleTime = mine.Where(d => d.OvertimeApprovedByUserId != null && d.OvertimeMultiplier >= 2m).Sum(d => d.OvertimeHours);
                decimal unapproved = mine.Where(d => d.OvertimeApprovedByUserId == null).Sum(d => d.OvertimeHours);

                // An absence is unpaid unless leave covers the day; unpaid leave counts its days starting in the period (as sick pay does).
                List<LeaveRecord> theirs = [.. leave.Where(l => l.EmployeeId == id)];
                decimal unpaid = mine.Count(d => d.Status == AttendanceStatus.Absent && !theirs.Any(l => l.FromDate <= d.Date && l.ToDate >= d.Date))
                    + theirs.Where(l => unpaidTypes.Contains(l.LeaveType.ToUpperInvariant()) && l.FromDate >= fromDate).Sum(l => l.Days);
                decimal deduction = Math.Round(unpaid * basic.GetValueOrDefault(id) / workingDaysPerMonth, 2, MidpointRounding.AwayFromZero);
                foreach ((string element, decimal? units, decimal? amount) in new (string, decimal?, decimal?)[]
                    { (overtimeElementCode, overtime, null), (doubleTimeElementCode, doubleTime, null), (unpaidElementCode, null, deduction) })
                {
                    if ((units ?? amount ?? 0m) <= 0m)
                    {
                        continue;
                    }

                    Result<long> set = await _variables.SetAsync(companyId, id, element, periodLabel, units, amount, ct).ConfigureAwait(false);
                    if (set.IsFailure)
                    {
                        return set.Error;
                    }
                }

                if (overtime + doubleTime + unapproved + unpaid > 0m)
                {
                    lines.Add(new AttendancePayLine(id, overtime, doubleTime, unapproved, unpaid, deduction));
                }
            }

            return Result<IReadOnlyList<AttendancePayLine>>.Success(lines);
        }, ct);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<SickPayLine>>> ApplySickPayAsync(long companyId, string leaveType, string periodLabel, DateOnly fromDate, DateOnly toDate, string deductionElementCode,
        decimal workingDaysPerMonth, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            string type = (leaveType ?? string.Empty).Trim().ToUpperInvariant();
            if (workingDaysPerMonth <= 0m || toDate < fromDate)
            {
                return Error.Validation("Pay.SickPayPeriod", "Sick pay needs a period and a positive number of working days a month.");
            }

            LeavePolicy? policy = await _db.Set<LeavePolicy>().Include(p => p.SickTiers).FirstOrDefaultAsync(p => p.CompanyId == companyId && p.LeaveType == type, ct).ConfigureAwait(false);
            if (policy is null || policy.SickTiers.Count == 0)
            {
                return Error.Rule("Pay.NoSickTiers", $"Leave type {type} has no sick pay tiers.");
            }

            // The leave year the period starts in.
            DateOnly yearStart = new(fromDate.Month >= policy.LeaveYearStartMonth ? fromDate.Year : fromDate.Year - 1, policy.LeaveYearStartMonth, 1);
            var records = await _db.LeaveRecords.AsNoTracking()
                .Where(l => l.CompanyId == companyId && l.Kind == LeaveRecordKind.Taken && l.LeaveType.ToUpper() == type && l.FromDate >= yearStart && l.FromDate <= toDate)
                .OrderBy(l => l.FromDate).ThenBy(l => l.Id).Select(l => new { l.EmployeeId, l.FromDate, l.Days }).ToListAsync(ct).ConfigureAwait(false);
            List<long> ids = [.. records.Where(r => r.FromDate >= fromDate).Select(r => r.EmployeeId).Distinct()];
            Dictionary<long, Employee> employees = await _db.Employees.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct).ConfigureAwait(false);

            List<SickPayLine> lines = [];
            foreach (long id in ids)
            {
                decimal before = records.Where(r => r.EmployeeId == id && r.FromDate < fromDate).Sum(r => r.Days);
                decimal inPeriod = records.Where(r => r.EmployeeId == id && r.FromDate >= fromDate).Sum(r => r.Days);
                decimal unpaid = UnpaidDays(policy.SickTiers, before, before + inPeriod);
                decimal deduction = Math.Round(unpaid * employees[id].BasicSalary / workingDaysPerMonth, 2, MidpointRounding.AwayFromZero);
                if (deduction > 0m)
                {
                    Result<long> set = await _variables.SetAsync(companyId, id, deductionElementCode, periodLabel, null, deduction, ct).ConfigureAwait(false);
                    if (set.IsFailure)
                    {
                        return set.Error;
                    }
                }

                lines.Add(new SickPayLine(id, inPeriod, unpaid, deduction));
            }

            return Result<IReadOnlyList<SickPayLine>>.Success(lines);
        }, ct);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<LeaveAdvanceLine>>> AdvanceLeavePayAsync(long companyId, string leaveType, DateOnly fromDate, DateOnly toDate, string periodLabel, string recoveryPeriodLabel,
        string advanceElementCode, string recoveryElementCode, decimal workingDaysPerMonth, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            string type = (leaveType ?? string.Empty).Trim().ToUpperInvariant();
            if (workingDaysPerMonth <= 0m || toDate < fromDate || string.IsNullOrWhiteSpace(periodLabel) || string.IsNullOrWhiteSpace(recoveryPeriodLabel)
                || string.Equals(periodLabel.Trim(), recoveryPeriodLabel.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Error.Validation("Pay.LeaveAdvance", "Leave pay in advance needs the leave window, a pay period and a later recovery period, and working days a month.");
            }

            var taken = await _db.LeaveRecords.AsNoTracking()
                .Where(l => l.CompanyId == companyId && l.Kind == LeaveRecordKind.Taken && l.LeaveType.ToUpper() == type && l.FromDate >= fromDate && l.FromDate <= toDate)
                .GroupBy(l => l.EmployeeId).Select(g => new { EmployeeId = g.Key, Days = g.Sum(l => l.Days) }).ToListAsync(ct).ConfigureAwait(false);
            List<long> ids = [.. taken.Select(t => t.EmployeeId)];
            Dictionary<long, decimal> basic = await _db.Employees.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.BasicSalary, ct).ConfigureAwait(false);

            List<LeaveAdvanceLine> lines = [];
            foreach (var leave in taken.OrderBy(t => t.EmployeeId))
            {
                decimal amount = Math.Round(leave.Days * basic.GetValueOrDefault(leave.EmployeeId) / workingDaysPerMonth, 2, MidpointRounding.AwayFromZero);
                if (amount <= 0m)
                {
                    continue;
                }

                foreach ((string element, string period) in new[] { (advanceElementCode, periodLabel), (recoveryElementCode, recoveryPeriodLabel) })
                {
                    Result<long> set = await _variables.SetAsync(companyId, leave.EmployeeId, element, period, null, amount, ct).ConfigureAwait(false);
                    if (set.IsFailure)
                    {
                        return set.Error;
                    }
                }

                lines.Add(new LeaveAdvanceLine(leave.EmployeeId, leave.Days, amount));
            }

            return Result<IReadOnlyList<LeaveAdvanceLine>>.Success(lines);
        }, ct);

    // The unpaid share of the sick days from position 'from' to 'to' in the leave year (day n spans n-1..n); days before the first tier are on full pay.
    private static decimal UnpaidDays(IReadOnlyList<LeaveSickTier> tiers, decimal from, decimal to)
    {
        decimal unpaid = 0m;
        for (int n = 0; n < tiers.Count; n++)
        {
            decimal start = tiers[n].FromDay - 1, end = n + 1 < tiers.Count ? tiers[n + 1].FromDay - 1 : decimal.MaxValue;
            decimal overlap = Math.Min(to, end) - Math.Max(from, start);
            if (overlap > 0m)
            {
                unpaid += overlap * (100m - tiers[n].PayPercent) / 100m;
            }
        }

        return decimal.Round(unpaid, 4, MidpointRounding.AwayFromZero);
    }

    /// <inheritdoc />
    public async Task<Result<decimal>> CreditTimeOffAsync(long companyId, string employeeNumber, decimal overtimeHours, decimal hoursPerDay, decimal ratio = 1m, string leaveType = "TOIL",
        CancellationToken ct = default)
    {
        decimal days = LeavePolicyRules.TimeOffInLieu(overtimeHours, hoursPerDay, ratio);
        if (days <= 0m)
        {
            return Error.Validation("Pay.Toil", "Time off in lieu needs overtime hours, the hours in a day and a ratio, all positive.");
        }

        string number = (employeeNumber ?? string.Empty).Trim().ToUpperInvariant(), type = (leaveType ?? "TOIL").Trim().ToUpperInvariant();
        long? employee = await _db.Employees.Where(e => e.CompanyId == companyId && e.EmployeeNumber == number).Select(e => (long?)e.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (employee is not long id)
        {
            return Error.NotFound("Pay.EmployeeNotFound", $"No employee {number}.");
        }

        EmployeeLeaveBalance? balance = await _db.Set<EmployeeLeaveBalance>().FirstOrDefaultAsync(b => b.CompanyId == companyId && b.EmployeeId == id && b.LeaveType == type, ct).ConfigureAwait(false);
        if (balance is null)
        {
            balance = new EmployeeLeaveBalance(companyId, id, type);
            _db.Set<EmployeeLeaveBalance>().Add(balance);
        }

        balance.Accrue(days);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return days;
    }
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
