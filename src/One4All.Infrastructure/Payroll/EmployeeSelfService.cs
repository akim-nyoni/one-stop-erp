using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Workflow;
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
    private readonly Lazy<IWorkflowService>? _workflow;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="attendance">The attendance service (approved leave is taken through it).</param>
    /// <param name="workflow">Routes applications through the approval workflow when one is defined for leave.</param>
    public EmployeeSelfService(One4AllDbContext db, IAttendanceService attendance, Lazy<IWorkflowService>? workflow = null)
    {
        _db = db;
        _attendance = attendance;
        _workflow = workflow;
    }

    /// <summary>The workflow reference of a leave application.</summary>
    /// <param name="applicationId">The application.</param>
    /// <returns>The reference.</returns>
    internal static string LeaveReference(long applicationId) => $"LV-{applicationId}";

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
        string? text = await StoredPayslips.TextAsync(_db, employee, period, ct).ConfigureAwait(false);
        if (text is null)
        {
            return Error.NotFound("Ess.NoPayslip", $"No payslip for {period}.");
        }

        DateOnly payDate = await _db.Payslips.Where(p => p.EmployeeId == employee.Id && p.PeriodLabel == period).MinAsync(p => p.PayDate, ct).ConfigureAwait(false);
        _db.PayslipPrints.Add(new PayslipPrint(employee.CompanyId, employee.Id, period, payDate));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return text;
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
    public Task<Result<long>> ApplyForLeaveAsync(long userId, string leaveType, DateOnly fromDate, DateOnly toDate, decimal days, CancellationToken ct = default)
        => _db.AtomicAsync(() => ApplyForLeaveCoreAsync(userId, leaveType, fromDate, toDate, days, ct), ct);

    // One unit of work: the application and its approval workflow (when leave has one) start together.
    private async Task<Result<long>> ApplyForLeaveCoreAsync(long userId, string leaveType, DateOnly fromDate, DateOnly toDate, decimal days, CancellationToken ct)
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

        // Department cover (brief §4.15): no more of the department away at once than the leave type's policy allows.
        string leaveCode = balance!.LeaveType;
        int? maxAway = await _db.Set<LeavePolicy>().Where(p => p.CompanyId == me.CompanyId && p.LeaveType == leaveCode).Select(p => p.MaxAwayPerDepartment).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (maxAway is int limit && !string.IsNullOrWhiteSpace(me.Department))
        {
            string department = me.Department;
            int away = await (from a in _db.LeaveApplications
                              join e in _db.Employees on a.EmployeeId equals e.Id
                              where a.CompanyId == me.CompanyId && e.Department == department && a.EmployeeId != me.Id && a.Status != SelfServiceStatus.Rejected
                                  && a.FromDate <= toDate && a.ToDate >= fromDate
                              select a.EmployeeId).Distinct().CountAsync(ct).ConfigureAwait(false);
            if (away >= limit)
            {
                return Error.Rule("Ess.LeaveCover", $"{away} of the {department} department are already away then; at most {limit} may be away at once.");
            }
        }

        LeaveApplication application = new(me.CompanyId, me.Id, balance.LeaveType, fromDate, toDate, days);
        _db.LeaveApplications.Add(application);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        Result<bool> governed = await Workflow.WorkflowGate.StartIfDefinedAsync(_db, _workflow, me.CompanyId, WorkflowDocumentTypes.LeaveApplication, LeaveReference(application.Id), days, userId,
            [("Department", me.Department), ("CostCentre", me.CostCentre), ("LeaveType", balance.LeaveType)], ct).ConfigureAwait(false);
        return governed.IsFailure ? Result<long>.Failure(governed.Error) : application.Id;
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
        string reference = LeaveReference(applicationId);
        if (await Workflow.WorkflowGate.InProgressAsync(_db, companyId, WorkflowDocumentTypes.LeaveApplication, reference, ct).ConfigureAwait(false))
        {
            return Workflow.WorkflowGate.Governed(WorkflowDocumentTypes.LeaveApplication, reference);
        }

        return await ConcludeLeaveAsync(companyId, applicationId, approve, deciderUserId, note, ct).ConfigureAwait(false);
    }

    /// <summary>Decides a leave application: approval takes the leave through attendance.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="applicationId">The application.</param>
    /// <param name="approve">True to approve.</param>
    /// <param name="deciderUserId">Who decided.</param>
    /// <param name="note">Their note.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new status.</returns>
    internal async Task<Result<SelfServiceStatus>> ConcludeLeaveAsync(long companyId, long applicationId, bool approve, long deciderUserId, string? note, CancellationToken ct)
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

/// <summary>Concludes a leave application's approval workflow: approval takes the leave, anything else declines it.</summary>
internal sealed class LeaveWorkflowHandler : IWorkflowDocumentHandler
{
    private readonly EmployeeSelfService _service;

    public LeaveWorkflowHandler(EmployeeSelfService service) => _service = service;

    public string DocumentType => WorkflowDocumentTypes.LeaveApplication;

    public async Task<Result> ConcludeAsync(long companyId, string documentReference, Core.Workflow.WorkflowStatus outcome, long decidedByUserId, string? comment, CancellationToken ct = default)
    {
        if (!long.TryParse(documentReference.AsSpan(3), out long applicationId))
        {
            return Result.Success();
        }

        Result<SelfServiceStatus> decided = await _service.ConcludeLeaveAsync(companyId, applicationId, outcome == Core.Workflow.WorkflowStatus.Approved, decidedByUserId, comment, ct).ConfigureAwait(false);
        return decided.IsFailure && decided.Error.Code != "Ess.RequestNotFound" ? Result.Failure(decided.Error) : Result.Success();
    }
}
