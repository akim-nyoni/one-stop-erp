using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Scheduling;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <summary>Finds what lapses for active employees and tells HR.</summary>
public sealed class HrAlertService : IHrAlertService
{
    /// <summary>The permission whose holders are told (anyone working on employee records).</summary>
    public const string HrPermission = "Payroll.Employee";

    private readonly One4AllDbContext _db;
    private readonly INotificationService _notifications;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="notifications">Delivers the in-app notifications.</param>
    public HrAlertService(One4AllDbContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HrExpiry>> ExpiringAsync(long companyId, DateOnly asAt, int withinDays, CancellationToken ct = default)
    {
        DateOnly until = asAt.AddDays(Math.Max(0, withinDays));
        List<Employee> staff = await _db.Employees.AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Status != EmployeeStatus.Terminated && e.TerminationDate == null).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, Employee> byId = staff.ToDictionary(e => e.Id);
        List<HrExpiry> due = [];
        void Add(Employee e, string what, DateOnly? on)
        {
            if (on is DateOnly date && date <= until)
            {
                due.Add(new HrExpiry(e.EmployeeNumber, e.FullName, what, date, date.DayNumber - asAt.DayNumber));
            }
        }

        foreach (Employee e in staff)
        {
            Add(e, "Work permit", e.WorkPermitExpiry);
            Add(e, $"{e.EmploymentType} contract ends", e.ContractEndDate);
            if (e.ProbationEndDate is DateOnly probation && probation >= asAt.AddDays(-30))
            {
                Add(e, "Probation ends — confirm or extend", probation);
            }
        }

        List<long> ids = [.. byId.Keys];
        foreach (EmployeeDocument d in await _db.Set<EmployeeDocument>().AsNoTracking()
            .Where(d => d.CompanyId == companyId && ids.Contains(d.EmployeeId) && d.ExpiresOn != null && d.ExpiresOn <= until).ToListAsync(ct).ConfigureAwait(false))
        {
            Add(byId[d.EmployeeId], $"{d.DocumentType}{(d.Reference is null ? string.Empty : " " + d.Reference)}", d.ExpiresOn);
        }

        foreach (TrainingRecord t in await _db.Set<TrainingRecord>().AsNoTracking()
            .Where(t => t.CompanyId == companyId && ids.Contains(t.EmployeeId) && t.ExpiresOn != null && t.ExpiresOn <= until).ToListAsync(ct).ConfigureAwait(false))
        {
            Add(byId[t.EmployeeId], $"Training: {t.Course}", t.ExpiresOn);
        }

        return [.. due.OrderBy(d => d.ExpiresOn).ThenBy(d => d.EmployeeNumber, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public async Task<Result<int>> NotifyAsync(long companyId, DateOnly asAt, int withinDays, CancellationToken ct = default)
    {
        IReadOnlyList<HrExpiry> due = await ExpiringAsync(companyId, asAt, withinDays, ct).ConfigureAwait(false);
        if (due.Count == 0)
        {
            return 0;
        }

        // HR: the active users whose roles cover employee records, and the system administrators.
        List<long> roleIds = await _db.Roles.Where(r => r.Permissions.Any(p => p.PermissionCode == HrPermission)).Select(r => r.Id).ToListAsync(ct).ConfigureAwait(false);
        List<long> users = await _db.Users.Where(u => u.IsActive && (u.IsSystemAdministrator || u.Roles.Any(r => roleIds.Contains(r.RoleId)))).Select(u => u.Id).ToListAsync(ct)
            .ConfigureAwait(false);
        string body = string.Join(Environment.NewLine, due.Select(d =>
            $"{d.EmployeeNumber} {d.EmployeeName}: {d.What} {(d.DaysLeft < 0 ? "lapsed" : "lapses")} {d.ExpiresOn:yyyy-MM-dd}{(d.DaysLeft < 0 ? string.Empty : $" ({d.DaysLeft} days)")}"));
        int sent = 0;
        foreach (long user in users)
        {
            Result<long> notified = await _notifications.NotifyUserAsync(companyId, user, $"{due.Count} HR item(s) expiring", body, ct).ConfigureAwait(false);
            if (notified.IsSuccess)
            {
                sent++;
            }
        }

        return sent;
    }
}

/// <summary>Scheduled task: tells HR what lapses within 30 days (key <c>hr.expiry-alerts</c>).</summary>
public sealed class HrExpiryAlertsTask : IScheduledTask
{
    private readonly IHrAlertService _alerts;

    /// <summary>Creates the task.</summary>
    /// <param name="alerts">The alert service.</param>
    public HrExpiryAlertsTask(IHrAlertService alerts) => _alerts = alerts;

    /// <inheritdoc />
    public string Key => "hr.expiry-alerts";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        Result<int> sent = await _alerts.NotifyAsync(companyId, DateOnly.FromDateTime(runAt.UtcDateTime), 30, ct).ConfigureAwait(false);
        return sent.IsFailure ? sent.Error : sent.Value == 0 ? "Nothing in HR expires within 30 days." : $"{sent.Value} HR user(s) told what expires within 30 days.";
    }
}
