using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Scheduling;
using One4All.Core.Budgeting;
using One4All.Core.Common;
using One4All.Core.Communications;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.Budgeting;

/// <summary>Budget preparation deadlines and reminders, and utilisation alerts emailed once per threshold.</summary>
public sealed class BudgetNotificationService : IBudgetNotificationService
{
    private static readonly decimal[] DefaultThresholds = [75m, 90m, 100m];

    private readonly One4AllDbContext _db;
    private readonly IBudgetControlService _control;
    private readonly INotificationService _notifications;
    private readonly IClock _clock;
    private readonly IBudgetingService? _budgets;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="control">Works out utilisation.</param>
    /// <param name="notifications">Queues the emails.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="budgets">The budgets compared with actuals for the budget-holder statements.</param>
    public BudgetNotificationService(One4AllDbContext db, IBudgetControlService control, INotificationService notifications, IClock clock, IBudgetingService budgets)
        : this(db, control, notifications, clock) => _budgets = budgets;

    /// <summary>Creates the service without budget-holder statements.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="control">The vote book.</param>
    /// <param name="notifications">The outbound messages.</param>
    /// <param name="clock">The clock.</param>
    public BudgetNotificationService(One4AllDbContext db, IBudgetControlService control, INotificationService notifications, IClock clock)
    {
        _db = db;
        _control = control;
        _notifications = notifications;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> SetDeadlineAsync(long companyId, string department, DateOnly? deadline, CancellationToken ct = default)
    {
        string key = (department ?? string.Empty).Trim();
        DepartmentUnit? unit = (await _db.DepartmentUnits.Where(u => u.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(u => string.Equals(u.Department, key, StringComparison.OrdinalIgnoreCase));
        if (unit is null)
        {
            return Result.Failure(Error.NotFound("Budget.DepartmentNotFound", $"Department {key} is not a preparation unit."));
        }

        unit.SubmissionDeadline = deadline;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PreparationStatus>> BoardAsync(long companyId, long fiscalYearId, DateOnly asAt, CancellationToken ct = default)
    {
        List<DepartmentUnit> units = await _db.DepartmentUnits.AsNoTracking().Where(u => u.CompanyId == companyId).OrderBy(u => u.Department).ToListAsync(ct).ConfigureAwait(false);
        var budgets = await _db.Budgets.AsNoTracking().Where(b => b.CompanyId == companyId && b.FiscalYearId == fiscalYearId && b.Department != null)
            .Select(b => new { b.Department, b.VersionNumber, b.Id, b.Status }).ToListAsync(ct).ConfigureAwait(false);
        return [.. units.Select(u =>
        {
            BudgetStatus? status = budgets.Where(b => string.Equals(b.Department, u.Department, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(b => b.VersionNumber).ThenByDescending(b => b.Id).Select(b => (BudgetStatus?)b.Status).FirstOrDefault();
            return new PreparationStatus(u.Department, u.BudgetHolder, u.SubmissionDeadline, status?.ToString(), u.SubmissionDeadline is DateOnly d ? d.DayNumber - asAt.DayNumber : null);
        })];
    }

    /// <inheritdoc />
    public async Task<Result<BudgetNotices>> RemindAsync(long companyId, long fiscalYearId, DateOnly asAt, int daysBefore, CancellationToken ct = default)
    {
        if (daysBefore < 0)
        {
            return Error.Validation("Budget.ReminderDays", "Reminders are sent some days before the deadline (zero or more).");
        }

        List<DepartmentUnit> units = await _db.DepartmentUnits.AsNoTracking().Where(u => u.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        int sent = 0, due = 0;
        foreach (PreparationStatus row in await BoardAsync(companyId, fiscalYearId, asAt, ct).ConfigureAwait(false))
        {
            if (row.DaysLeft is not int left || left > daysBefore || (row.Status is not null && row.Status != nameof(BudgetStatus.Draft)))
            {
                continue;
            }

            due++;
            string? email = units.First(u => u.Department == row.Department).BudgetHolderEmail;
            if (string.IsNullOrWhiteSpace(email))
            {
                continue;
            }

            string when = left < 0 ? $"was due {-left} day(s) ago, on {row.Deadline:yyyy-MM-dd}" : left == 0 ? "is due today" : $"is due in {left} day(s), on {row.Deadline:yyyy-MM-dd}";
            Result<long> queued = await _notifications.QueueDirectAsync(companyId, $"Budget reminder {row.Department}", MessageChannel.Email, [email.Trim()],
                $"Budget submission for {row.Department} {(left < 0 ? "overdue" : "due")}",
                $"Dear {row.BudgetHolder ?? "budget holder"},{Environment.NewLine}{Environment.NewLine}The {row.Department} budget submission {when}. It is {(row.Status is null ? "not started" : "still in draft")}.",
                null, ct).ConfigureAwait(false);
            if (queued.IsFailure)
            {
                return queued.Error;
            }

            sent++;
        }

        return new BudgetNotices(sent, due);
    }

    /// <inheritdoc />
    public async Task<Result<BudgetNotices>> AlertAsync(long companyId, DateOnly asAt, CancellationToken ct = default)
    {
        // Who hears: the company's alert address for every vote, and each department's budget holder for the votes in that department's budget,
        // at the department's own thresholds when it has them (brief §4.18.5).
        Company company = await _db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        List<DepartmentUnit> holders = await _db.DepartmentUnits.AsNoTracking().Where(u => u.CompanyId == companyId && u.BudgetHolderEmail != null && u.BudgetHolderEmail != string.Empty)
            .ToListAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(company.BudgetAlertEmail) && holders.Count == 0)
        {
            return Error.Rule("Budget.NoAlertEmail", "Set the address budget alerts are sent to first.");
        }

        FiscalYear? year = await _db.FiscalYears.AsNoTracking().Include(y => y.Periods)
            .FirstOrDefaultAsync(y => y.CompanyId == companyId && y.StartDate <= asAt && y.EndDate >= asAt, ct).ConfigureAwait(false);
        FiscalPeriod? period = year?.Periods.FirstOrDefault(p => p.Contains(asAt));
        if (year is null || period is null)
        {
            return Error.NotFound("Budget.NoYear", $"No fiscal period contains {asAt:yyyy-MM-dd}.");
        }

        IReadOnlyList<decimal> companyThresholds = Thresholds(company.BudgetAlertThresholds);
        Dictionary<string, IReadOnlyList<decimal>> departmentThresholds = holders.ToDictionary(h => h.Department, h => string.IsNullOrWhiteSpace(h.AlertThresholds) ? companyThresholds : Thresholds(h.AlertThresholds),
            StringComparer.OrdinalIgnoreCase);
        List<decimal> every = [.. companyThresholds.Concat(departmentThresholds.Values.SelectMany(t => t)).Distinct().Order()];
        IReadOnlyList<UtilisationAlert> alerts = await _control.UtilisationAlertsAsync(companyId, year.Id, period.PeriodNumber, every, ct).ConfigureAwait(false);
        HashSet<(string, decimal)> sentBefore = [.. (await _db.BudgetAlertsSent.AsNoTracking().Where(a => a.CompanyId == companyId && a.FiscalYearId == year.Id).ToListAsync(ct).ConfigureAwait(false))
            .Select(a => (a.AccountCode, a.Threshold))];
        List<UtilisationAlert> fresh = [.. alerts.Where(a => !sentBefore.Contains((a.AccountCode, a.ThresholdCrossed)))];
        if (fresh.Count == 0)
        {
            return new BudgetNotices(0, 0);
        }

        // The departments whose budgets carry each vote this year, for routing to their holders.
        Dictionary<string, HashSet<string>> departmentsByVote = new(StringComparer.OrdinalIgnoreCase);
        foreach (var budget in await _db.Budgets.AsNoTracking().Where(b => b.CompanyId == companyId && b.FiscalYearId == year.Id && b.Department != null)
            .Select(b => new { b.Department, Accounts = b.Lines.Select(l => l.AccountCode).Distinct().ToList() }).ToListAsync(ct).ConfigureAwait(false))
        {
            foreach (string account in budget.Accounts)
            {
                if (!departmentsByVote.TryGetValue(account, out HashSet<string>? set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    departmentsByVote[account] = set;
                }

                set.Add(budget.Department!);
            }
        }

        string Describe(UtilisationAlert alert) => $"{alert.AccountCode} {alert.Name}: {alert.PercentUsed:0.#}% used ({alert.Used:N2} of {alert.Appropriation:N2}) — past {alert.ThresholdCrossed:0.#}%";
        async Task<Error?> SendAsync(string recipient, string who, IReadOnlyList<UtilisationAlert> theirs)
        {
            if (theirs.Count == 0)
            {
                return null;
            }

            StringBuilder body = new($"Votes that have crossed a utilisation threshold, as at {asAt:yyyy-MM-dd} (period {period.PeriodNumber} of {year.Code}){who}:{Environment.NewLine}{Environment.NewLine}");
            foreach (UtilisationAlert alert in theirs)
            {
                body.AppendLine(Describe(alert));
            }

            Result<long> queued = await _notifications.QueueDirectAsync(companyId, $"Budget alerts {asAt:yyyy-MM-dd}", MessageChannel.Email, [recipient.Trim()],
                $"Budget utilisation alerts: {theirs.Count} vote(s)", body.ToString(), null, ct).ConfigureAwait(false);
            return queued.IsFailure ? queued.Error : null;
        }

        if (!string.IsNullOrWhiteSpace(company.BudgetAlertEmail)
            && await SendAsync(company.BudgetAlertEmail, string.Empty, [.. fresh.Where(a => companyThresholds.Contains(a.ThresholdCrossed))]).ConfigureAwait(false) is Error companyError)
        {
            return companyError;
        }

        foreach (DepartmentUnit holder in holders)
        {
            IReadOnlyList<decimal> mine = departmentThresholds[holder.Department];
            List<UtilisationAlert> theirs = [.. fresh.Where(a => mine.Contains(a.ThresholdCrossed) && departmentsByVote.GetValueOrDefault(a.AccountCode)?.Contains(holder.Department) == true)];
            if (await SendAsync(holder.BudgetHolderEmail!, $" for {holder.Department}", theirs).ConfigureAwait(false) is Error holderError)
            {
                return holderError;
            }
        }

        foreach (UtilisationAlert alert in fresh)
        {
            _db.BudgetAlertsSent.Add(new BudgetAlertSent(companyId, year.Id, alert.AccountCode, alert.ThresholdCrossed, _clock.UtcNow));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new BudgetNotices(1, fresh.Count);
    }

    /// <inheritdoc />
    public async Task<Result<BudgetNotices>> HolderStatementsAsync(long companyId, DateOnly asAt, CancellationToken ct = default)
    {
        List<DepartmentUnit> units = await _db.DepartmentUnits.AsNoTracking().Where(u => u.CompanyId == companyId && u.BudgetHolderEmail != null).OrderBy(u => u.Department)
            .ToListAsync(ct).ConfigureAwait(false);
        int sent = 0;
        foreach (DepartmentUnit unit in units.Where(u => !string.IsNullOrWhiteSpace(u.BudgetHolderEmail)))
        {
            Result<string> statement = await HolderStatementAsync(companyId, unit.Department, asAt, ct).ConfigureAwait(false);
            if (statement.IsFailure)
            {
                continue;
            }

            Result<long> queued = await _notifications.QueueDirectAsync(companyId, $"Budget statement {unit.Department} {asAt:yyyy-MM}", MessageChannel.Email, [unit.BudgetHolderEmail!.Trim()],
                $"Budget statement for {unit.Department}, {asAt:MMMM yyyy}", $"Dear {unit.BudgetHolder ?? "budget holder"},{Environment.NewLine}{Environment.NewLine}{statement.Value}", null, ct)
                .ConfigureAwait(false);
            if (queued.IsFailure)
            {
                return queued.Error;
            }

            sent++;
        }

        return new BudgetNotices(sent, units.Count);
    }

    /// <inheritdoc />
    public async Task<Result<string>> HolderStatementAsync(long companyId, string department, DateOnly asAt, CancellationToken ct = default)
    {
        if (_budgets is null)
        {
            return Error.Rule("Budget.NoStatements", "Budget-holder statements are not available here.");
        }

        string key = (department ?? string.Empty).Trim();
        FiscalYear? year = await _db.FiscalYears.AsNoTracking().Include(y => y.Periods)
            .FirstOrDefaultAsync(y => y.CompanyId == companyId && y.StartDate <= asAt && y.EndDate >= asAt, ct).ConfigureAwait(false);
        FiscalPeriod? period = year?.Periods.FirstOrDefault(p => p.Contains(asAt));
        if (year is null || period is null)
        {
            return Error.NotFound("Budget.NoYear", $"No fiscal period contains {asAt:yyyy-MM-dd}.");
        }

        Result<BudgetVsActualReport> toDate = await _budgets.BudgetVsActualAsync(companyId, year.Id, BudgetType.OriginalApproved, key, period.PeriodNumber, ct).ConfigureAwait(false);
        if (toDate.IsFailure)
        {
            return toDate.Error;
        }

        int lastPeriod = year.Periods.Max(p => p.PeriodNumber);
        Result<BudgetVsActualReport> wholeYear = await _budgets.BudgetVsActualAsync(companyId, year.Id, BudgetType.OriginalApproved, key, lastPeriod, ct).ConfigureAwait(false);
        Dictionary<string, decimal> annual = wholeYear.IsSuccess ? wholeYear.Value.Rows.ToDictionary(r => r.AccountCode, r => r.Budget) : [];
        Dictionary<string, string> names = await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId).ToDictionaryAsync(a => a.AccountCode, a => a.Name, ct).ConfigureAwait(false);
        Company company = await _db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        decimal[] thresholds = Thresholds(company.BudgetAlertThresholds);

        StringBuilder text = new($"Budget statement for {key}, period {period.PeriodNumber} of {year.Code} (to {period.EndDate:yyyy-MM-dd}).{Environment.NewLine}{Environment.NewLine}");
        text.AppendLine(CultureInfo.InvariantCulture, $"{"Account",-10} {"Name",-28} {"Budget to date",15} {"Actual to date",15} {"Variance",13} {"Year used",9}");
        List<string> flagged = [];
        foreach (BudgetVsActualRow row in toDate.Value.Rows.OrderBy(r => r.AccountCode, StringComparer.Ordinal))
        {
            decimal year12 = annual.GetValueOrDefault(row.AccountCode, row.Budget);
            decimal used = year12 == 0m ? 0m : decimal.Round(row.Actual / year12 * 100m, 1, MidpointRounding.AwayFromZero);
            string name = names.GetValueOrDefault(row.AccountCode, "?");
            text.AppendLine(CultureInfo.InvariantCulture, $"{row.AccountCode,-10} {(name.Length > 28 ? name[..28] : name),-28} {row.Budget,15:N2} {row.Actual,15:N2} {row.Budget - row.Actual,13:N2} {used,8:0.0}%");
            if (thresholds.Where(t => used >= t).DefaultIfEmpty(0m).Max() is decimal crossed && crossed > 0m)
            {
                flagged.Add($"{row.AccountCode} {name}: {used:0.0}% of the year's budget used — past {crossed:0.#}%");
            }
        }

        decimal budget = toDate.Value.Rows.Sum(r => r.Budget), actual = toDate.Value.Rows.Sum(r => r.Actual);
        text.AppendLine(CultureInfo.InvariantCulture, $"{"Total",-39} {budget,15:N2} {actual,15:N2} {budget - actual,13:N2}");
        if (flagged.Count > 0)
        {
            text.AppendLine().AppendLine("Past a utilisation threshold:");
            flagged.ForEach(f => text.AppendLine(CultureInfo.InvariantCulture, $"  {f}"));
        }

        return text.ToString();
    }

    // The company's thresholds ("75, 90, 100"), or 75 / 90 / 100.
    private static decimal[] Thresholds(string? text)
    {
        decimal[] parsed = [.. (text ?? string.Empty).Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => decimal.TryParse(t.Trim().TrimEnd('%'), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal v) ? v : -1m).Where(v => v > 0m).Order()];
        return parsed.Length == 0 ? DefaultThresholds : parsed;
    }
}

/// <summary>Sends the day's budget alerts and deadline reminders (scheduled task <c>budget.notices</c>).</summary>
public sealed class BudgetNoticesTask : IScheduledTask
{
    private readonly IBudgetNotificationService _notices;
    private readonly One4AllDbContext _db;

    /// <summary>Creates the task.</summary>
    /// <param name="notices">The budget notices.</param>
    /// <param name="db">The database context.</param>
    public BudgetNoticesTask(IBudgetNotificationService notices, One4AllDbContext db)
    {
        _notices = notices;
        _db = db;
    }

    /// <inheritdoc />
    public string Key => "budget.notices";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(runAt.UtcDateTime);
        List<string> done = [];
        Result<BudgetNotices> alerts = await _notices.AlertAsync(companyId, today, ct).ConfigureAwait(false);
        done.Add(alerts.IsSuccess ? $"{alerts.Value.Items} new utilisation alert(s)" : alerts.Error.Message);
        if (today.Day == 1)
        {
            Result<BudgetNotices> statements = await _notices.HolderStatementsAsync(companyId, today.AddDays(-1), ct).ConfigureAwait(false);
            done.Add(statements.IsSuccess ? $"{statements.Value.Messages} budget-holder statement(s)" : statements.Error.Message);
        }

        // Reminders for the year being prepared: the next fiscal year if there is one, else this one.
        long? preparing = await _db.FiscalYears.AsNoTracking().Where(y => y.CompanyId == companyId && y.EndDate >= today).OrderByDescending(y => y.StartDate > today).ThenBy(y => y.StartDate)
            .Select(y => (long?)y.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (preparing is long year)
        {
            Result<BudgetNotices> reminders = await _notices.RemindAsync(companyId, year, today, 7, ct).ConfigureAwait(false);
            done.Add(reminders.IsSuccess ? $"{reminders.Value.Messages} reminder(s) for {reminders.Value.Items} department(s) due" : reminders.Error.Message);
        }

        return string.Join("; ", done) + ".";
    }
}
