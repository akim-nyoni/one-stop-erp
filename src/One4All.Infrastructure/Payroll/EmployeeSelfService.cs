using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Payroll;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <summary>
/// Employee self-service (brief §4.15.7). Every employee-facing call finds the employee through the signed-in user, and HR decisions are
/// maker-checker; an approved leave application is taken through the attendance service, an approved detail change is applied to the
/// employee (and so lands in the audit trail and the bank-detail change reports).
/// </summary>
public sealed class EmployeeSelfService : IEmployeeSelfService
{
    /// <summary>The details an employee may ask to change.</summary>
    public static readonly IReadOnlyList<string> ChangeableFields = ["BankName", "BankBranch", "BankAccountNumber", "MobileMoneyNumber"];

    private readonly One4AllDbContext _db;
    private readonly IAttendanceService _attendance;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="attendance">The attendance service (approved leave is taken through it).</param>
    public EmployeeSelfService(One4AllDbContext db, IAttendanceService attendance)
    {
        _db = db;
        _attendance = attendance;
    }

    /// <inheritdoc />
    public async Task<Result<SelfServiceProfile>> ProfileAsync(long userId, CancellationToken ct = default)
    {
        Result<Employee> me = await MeAsync(userId, ct).ConfigureAwait(false);
        if (me.IsFailure)
        {
            return me.Error;
        }

        Employee e = me.Value;
        string? masked = e.BankAccountNumber is { Length: > 4 } account ? new string('*', account.Length - 4) + account[^4..] : e.BankAccountNumber;
        return new SelfServiceProfile(e.CompanyId, e.Id, e.EmployeeNumber, e.FullName, e.Department, e.PaymentMethod.ToString(), masked);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PayslipSummary>>> PayslipsAsync(long userId, CancellationToken ct = default)
    {
        Result<Employee> me = await MeAsync(userId, ct).ConfigureAwait(false);
        if (me.IsFailure)
        {
            return me.Error;
        }

        List<Payslip> slips = await _db.Payslips.AsNoTracking().Where(p => p.CompanyId == me.Value.CompanyId && p.EmployeeId == me.Value.Id).ToListAsync(ct).ConfigureAwait(false);
        List<PayslipSummary> periods = [.. slips.GroupBy(p => p.PeriodLabel).Select(g => new PayslipSummary(g.Key, g.Min(p => p.PayDate), g.Sum(p => p.GrossEarnings), g.Sum(p => p.TotalDeductions), g.Sum(p => p.NetPay)))
            .OrderByDescending(p => p.PayDate)];
        return periods;
    }

    /// <inheritdoc />
    public async Task<Result<string>> PayslipTextAsync(long userId, string periodLabel, CancellationToken ct = default)
    {
        Result<Employee> me = await MeAsync(userId, ct).ConfigureAwait(false);
        if (me.IsFailure)
        {
            return me.Error;
        }

        Employee employee = me.Value;
        string period = (periodLabel ?? string.Empty).Trim();
        List<Payslip> slips = await _db.Payslips.AsNoTracking().Include(p => p.Lines).Where(p => p.CompanyId == employee.CompanyId && p.EmployeeId == employee.Id && p.PeriodLabel == period)
            .ToListAsync(ct).ConfigureAwait(false);
        if (slips.Count == 0)
        {
            return Error.NotFound("Ess.NoPayslip", $"No payslip for {period}.");
        }

        List<PayslipLine> lines = [.. slips.SelectMany(p => p.Lines)];
        IReadOnlyList<PayslipItem> Items(PayElementType type) => [.. lines.Where(l => l.Type == type).GroupBy(l => l.ElementCode).Select(g => new PayslipItem(g.First().Description, g.Sum(l => l.Amount)))
            .Where(i => i.Amount != 0m)];
        string company = await _db.Companies.Where(c => c.Id == employee.CompanyId).Select(c => c.Name).FirstAsync(ct).ConfigureAwait(false);
        _db.PayslipPrints.Add(new PayslipPrint(employee.CompanyId, employee.Id, period, slips.Min(p => p.PayDate)));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return PayslipFormatter.ToPlainText(new PayslipDocument
        {
            CompanyName = company, EmployeeName = employee.FullName, EmployeeNumber = employee.EmployeeNumber, PeriodLabel = period, PayDate = slips.Min(p => p.PayDate),
            Earnings = Items(PayElementType.Earning), Deductions = Items(PayElementType.Deduction), GrossEarnings = slips.Sum(p => p.GrossEarnings), TotalDeductions = slips.Sum(p => p.TotalDeductions),
            NetPay = slips.Sum(p => p.NetPay),
        });
    }

    /// <inheritdoc />
    public async Task<Result<TaxCertificate>> TaxCertificateAsync(long userId, int year, CancellationToken ct = default)
    {
        Result<Employee> me = await MeAsync(userId, ct).ConfigureAwait(false);
        if (me.IsFailure)
        {
            return me.Error;
        }

        Employee e = me.Value;
        DateOnly from = new(year, 1, 1), to = new(year, 12, 31);
        List<PayslipLine> lines = await _db.Payslips.AsNoTracking().Where(p => p.CompanyId == e.CompanyId && p.EmployeeId == e.Id && p.PayDate >= from && p.PayDate <= to).SelectMany(p => p.Lines)
            .ToListAsync(ct).ConfigureAwait(false);
        return new TaxCertificate(year, e.EmployeeNumber, e.FullName, e.TaxNumber, lines.Where(l => l.Type == PayElementType.Earning).Sum(l => l.Amount),
            lines.Where(l => l.Type == PayElementType.Earning && l.Taxable).Sum(l => l.Amount), lines.Where(l => TaxLines.IsTax(l.Type, l.ElementCode, l.Description)).Sum(l => l.Amount));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<LeaveBalanceView>>> LeaveBalancesAsync(long userId, CancellationToken ct = default)
    {
        Result<Employee> me = await MeAsync(userId, ct).ConfigureAwait(false);
        if (me.IsFailure)
        {
            return me.Error;
        }

        long id = me.Value.Id;
        List<EmployeeLeaveBalance> balances = await _db.EmployeeLeaveBalances.AsNoTracking().Where(b => b.EmployeeId == id).OrderBy(b => b.LeaveType).ToListAsync(ct).ConfigureAwait(false);
        List<LeaveApplication> pending = await _db.LeaveApplications.AsNoTracking().Where(a => a.EmployeeId == id && a.Status == SelfServiceStatus.Pending).ToListAsync(ct).ConfigureAwait(false);
        List<LeaveBalanceView> views = [.. balances.Select(b => new LeaveBalanceView(b.LeaveType, b.AccruedDays, b.TakenDays, b.BalanceDays,
            pending.Where(a => string.Equals(a.LeaveType, b.LeaveType, StringComparison.OrdinalIgnoreCase)).Sum(a => a.Days)))];
        return views;
    }

    /// <inheritdoc />
    public async Task<Result<long>> ApplyForLeaveAsync(long userId, string leaveType, DateOnly fromDate, DateOnly toDate, decimal days, CancellationToken ct = default)
    {
        Result<IReadOnlyList<LeaveBalanceView>> balances = await LeaveBalancesAsync(userId, ct).ConfigureAwait(false);
        if (balances.IsFailure)
        {
            return balances.Error;
        }

        if (toDate < fromDate || days <= 0m || days > toDate.DayNumber - fromDate.DayNumber + 1)
        {
            return Error.Validation("Hr.LeaveDates", "Leave needs an end on or after its start and positive days no more than the days in the range.");
        }

        string type = (leaveType ?? string.Empty).Trim().ToUpperInvariant();
        LeaveBalanceView? balance = balances.Value.FirstOrDefault(b => string.Equals(b.LeaveType, type, StringComparison.OrdinalIgnoreCase));
        decimal available = (balance?.Balance ?? 0m) - (balance?.Pending ?? 0m);
        if (days > available)
        {
            return Error.Rule("Ess.LeaveBalance", $"Only {available:0.##} day(s) of {type} leave are available after applications already made.");
        }

        Employee me = (await MeAsync(userId, ct).ConfigureAwait(false)).Value;
        LeaveApplication application = new(me.CompanyId, me.Id, balance!.LeaveType, fromDate, toDate, days);
        _db.LeaveApplications.Add(application);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return application.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> RequestDetailChangeAsync(long userId, string field, string newValue, CancellationToken ct = default)
    {
        Result<Employee> me = await MeAsync(userId, ct).ConfigureAwait(false);
        if (me.IsFailure)
        {
            return me.Error;
        }

        string? name = ChangeableFields.FirstOrDefault(f => string.Equals(f, field?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return Error.Validation("Ess.Field", $"Only {string.Join(", ", ChangeableFields)} can be changed through self-service.");
        }

        if (string.IsNullOrWhiteSpace(newValue))
        {
            return Error.Validation("Ess.Value", "The new value is required.");
        }

        DetailChangeRequest request = new(me.Value.CompanyId, me.Value.Id, name, Read(me.Value, name), newValue.Trim());
        _db.DetailChangeRequests.Add(request);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return request.Id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SelfServiceRequest>> PendingAsync(long companyId, CancellationToken ct = default)
    {
        Dictionary<long, Employee> employees = await _db.Employees.AsNoTracking().Where(e => e.CompanyId == companyId).ToDictionaryAsync(e => e.Id, ct).ConfigureAwait(false);
        List<LeaveApplication> leave = await _db.LeaveApplications.AsNoTracking().Where(a => a.CompanyId == companyId && a.Status == SelfServiceStatus.Pending).OrderBy(a => a.Id).ToListAsync(ct).ConfigureAwait(false);
        List<DetailChangeRequest> changes = await _db.DetailChangeRequests.AsNoTracking().Where(r => r.CompanyId == companyId && r.Status == SelfServiceStatus.Pending).OrderBy(r => r.Id).ToListAsync(ct)
            .ConfigureAwait(false);
        return [.. leave.Select(a => new SelfServiceRequest("Leave", a.Id, employees[a.EmployeeId].EmployeeNumber, employees[a.EmployeeId].FullName,
                $"{a.Days:0.##} day(s) {a.LeaveType} {a.FromDate:yyyy-MM-dd} to {a.ToDate:yyyy-MM-dd}")),
            .. changes.Select(r => new SelfServiceRequest("Detail", r.Id, employees[r.EmployeeId].EmployeeNumber, employees[r.EmployeeId].FullName, $"{r.Field}: {r.OldValue ?? "(none)"} -> {r.NewValue}"))];
    }

    /// <inheritdoc />
    public Task<Result<SelfServiceStatus>> DecideLeaveAsync(long companyId, long applicationId, bool approve, long deciderUserId, string? note, CancellationToken ct = default)
        => _db.AtomicAsync(() => DecideLeaveCoreAsync(companyId, applicationId, approve, deciderUserId, note, ct), ct);

    /// <inheritdoc />
    public async Task<Result<SelfServiceStatus>> DecideDetailChangeAsync(long companyId, long requestId, bool approve, long deciderUserId, string? note, CancellationToken ct = default)
    {
        DetailChangeRequest? request = await _db.DetailChangeRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (request is null || request.Status != SelfServiceStatus.Pending)
        {
            return Error.NotFound("Ess.RequestNotFound", $"No pending detail change {requestId}.");
        }

        Employee employee = await _db.Employees.FirstAsync(e => e.Id == request.EmployeeId, ct).ConfigureAwait(false);
        if (employee.UserId == deciderUserId)
        {
            return Error.Rule("Ess.OwnRequest", "Nobody can decide their own self-service request.");
        }

        if (approve)
        {
            Write(employee, request.Field, request.NewValue);
        }

        request.Decide(approve, deciderUserId, note);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return request.Status;
    }

    private async Task<Result<SelfServiceStatus>> DecideLeaveCoreAsync(long companyId, long applicationId, bool approve, long deciderUserId, string? note, CancellationToken ct)
    {
        LeaveApplication? application = await _db.LeaveApplications.FirstOrDefaultAsync(a => a.Id == applicationId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (application is null || application.Status != SelfServiceStatus.Pending)
        {
            return Error.NotFound("Ess.RequestNotFound", $"No pending leave application {applicationId}.");
        }

        long? owner = await _db.Employees.Where(e => e.Id == application.EmployeeId).Select(e => e.UserId).FirstAsync(ct).ConfigureAwait(false);
        if (owner == deciderUserId)
        {
            return Error.Rule("Ess.OwnRequest", "Nobody can decide their own self-service request.");
        }

        long? record = null;
        if (approve)
        {
            Result<long> taken = await _attendance.TakeLeaveAsync(companyId, application.EmployeeId, application.LeaveType, application.FromDate, application.ToDate, application.Days, ct).ConfigureAwait(false);
            if (taken.IsFailure)
            {
                return taken.Error;
            }

            record = taken.Value;
        }

        application.Decide(approve, deciderUserId, note, record);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return application.Status;
    }

    private async Task<Result<Employee>> MeAsync(long userId, CancellationToken ct)
    {
        Employee? employee = await _db.Employees.Where(e => e.UserId == userId && e.Status != EmployeeStatus.Terminated).OrderBy(e => e.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return employee is null ? Error.NotFound("Ess.NotAnEmployee", "This login is not linked to an employee.") : employee;
    }

    private static string? Read(Employee e, string field) => field switch
    {
        "BankName" => e.BankName,
        "BankBranch" => e.BankBranch,
        "BankAccountNumber" => e.BankAccountNumber,
        "MobileMoneyNumber" => e.MobileMoneyNumber,
        _ => null,
    };

    private static void Write(Employee e, string field, string value)
    {
        switch (field)
        {
            case "BankName":
                e.BankName = value;
                break;
            case "BankBranch":
                e.BankBranch = value;
                break;
            case "BankAccountNumber":
                e.BankAccountNumber = value;
                break;
            case "MobileMoneyNumber":
                e.MobileMoneyNumber = value;
                break;
        }
    }
}
