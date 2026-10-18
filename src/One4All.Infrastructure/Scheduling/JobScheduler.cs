using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Scheduling;
using One4All.Application.Scheduling;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Scheduling;

/// <summary>Runs registered tasks on their companies' schedules and keeps every run.</summary>
public sealed class JobScheduler : IJobScheduler
{
    private readonly One4AllDbContext _db;
    private readonly Dictionary<string, IScheduledTask> _tasks;
    private readonly Application.Modules.IModuleRegistry? _modules;
    private readonly Application.Abstractions.Licensing.ILicenceService? _licence;

    /// <summary>Creates the scheduler.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="tasks">The registered tasks.</param>
    /// <param name="modules">The module registry (gates tasks by their module's licence).</param>
    /// <param name="licence">The licence.</param>
    public JobScheduler(One4AllDbContext db, IEnumerable<IScheduledTask> tasks, Application.Modules.IModuleRegistry? modules = null, Application.Abstractions.Licensing.ILicenceService? licence = null)
    {
        _modules = modules;
        _licence = licence;
        ArgumentNullException.ThrowIfNull(tasks);
        _db = db;
        _tasks = new Dictionary<string, IScheduledTask>(StringComparer.OrdinalIgnoreCase);
        foreach (IScheduledTask task in tasks)
        {
            _tasks[task.Key] = task;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> TaskKeys => [.. _tasks.Keys.Order(StringComparer.Ordinal)];

    /// <inheritdoc />
    public async Task<Result<DateTimeOffset>> DefineAsync(JobDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Code) || !Enum.IsDefined(definition.Frequency)
            || (definition.Frequency == JobFrequency.Weekly && definition.DayOfWeek is null)
            || (definition.Frequency == JobFrequency.Monthly && definition.DayOfMonth is not (>= 1 and <= 31)))
        {
            return Error.Validation("Job.Definition", "A job needs a code and a frequency (a day of the week for weekly, a day 1–31 for monthly).");
        }

        if (!_tasks.TryGetValue(definition.TaskKey ?? string.Empty, out IScheduledTask? task))
        {
            return Error.NotFound("Job.UnknownTask", $"No task \"{definition.TaskKey}\" is registered. Known: {string.Join(", ", TaskKeys)}.");
        }

        string code = definition.Code.Trim().ToUpperInvariant();
        if (await _db.Set<ScheduledJob>().AnyAsync(j => j.CompanyId == definition.CompanyId && j.Code == code, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Job.Exists", $"Job {code} already exists.");
        }

        DateTimeOffset first = JobSchedule.NextRun(definition.Frequency, definition.TimeOfDay, definition.DayOfWeek, definition.DayOfMonth, definition.From);
        _db.Set<ScheduledJob>().Add(new ScheduledJob(definition.CompanyId, code, task.Key, definition.Frequency, definition.TimeOfDay, definition.DayOfWeek, definition.DayOfMonth, first));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return first;
    }

    /// <inheritdoc />
    public async Task<Result> SetEnabledAsync(long companyId, string code, bool enabled, DateTimeOffset now, CancellationToken ct = default)
    {
        Result<ScheduledJob> found = await FindAsync(companyId, code, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        ScheduledJob job = found.Value;
        job.SetEnabled(enabled, enabled ? JobSchedule.NextRun(job.Frequency, job.TimeOfDay, job.DayOfWeek, job.DayOfMonth, now) : job.NextRunAt);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<SchedulerPass> RunDueAsync(DateTimeOffset now, long? companyId = null, CancellationToken ct = default)
    {
        List<ScheduledJob> due = await _db.Set<ScheduledJob>().IgnoreQueryFilters([Persistence.One4AllDbContext.SoftDeleteFilter]).Where(j => j.IsEnabled && j.NextRunAt <= now && (companyId == null || j.CompanyId == companyId))
            .OrderBy(j => j.NextRunAt).ThenBy(j => j.Id).ToListAsync(ct).ConfigureAwait(false);
        int failed = 0;
        foreach (ScheduledJob job in due)
        {
            JobRunEntry run = await ExecuteAsync(job, now, ct).ConfigureAwait(false);
            Track(job);
            job.Ran(now, run.Succeeded, JobSchedule.NextRun(job.Frequency, job.TimeOfDay, job.DayOfWeek, job.DayOfMonth, now));
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            failed += run.Succeeded ? 0 : 1;
        }

        return new SchedulerPass(due.Count, failed);
    }

    /// <inheritdoc />
    public async Task<Result<JobRunEntry>> RunNowAsync(long companyId, string code, DateTimeOffset now, CancellationToken ct = default)
    {
        Result<ScheduledJob> found = await FindAsync(companyId, code, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        JobRunEntry run = await ExecuteAsync(found.Value, now, ct).ConfigureAwait(false);
        Track(found.Value);
        found.Value.Ran(now, run.Succeeded, found.Value.NextRunAt);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return run;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<JobRunEntry>>> HistoryAsync(long companyId, string code, CancellationToken ct = default)
    {
        Result<ScheduledJob> found = await FindAsync(companyId, code, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        long jobId = found.Value.Id;
        return Result<IReadOnlyList<JobRunEntry>>.Success(await _db.Set<JobRun>().AsNoTracking().Where(r => r.ScheduledJobId == jobId)
            .OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).Select(r => new JobRunEntry(r.StartedAt, r.Succeeded, r.Message)).ToListAsync(ct).ConfigureAwait(false));
    }

    // Runs the task, never letting it take the scheduler down, and keeps the run.
    private async Task<JobRunEntry> ExecuteAsync(ScheduledJob job, DateTimeOffset now, CancellationToken ct)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        bool succeeded;
        string message;
        if (!_tasks.TryGetValue(job.TaskKey, out IScheduledTask? task))
        {
            (succeeded, message) = (false, $"Task \"{job.TaskKey}\" is no longer registered.");
        }
        else if (_licence is { Enforced: true } && _modules?.ForTask(job.TaskKey) is Application.Modules.IModuleDescriptor owner && !_licence.IsModuleLicensed(owner.LicenceModule))
        {
            // A module's jobs run only while it is licensed (brief §11.5).
            (succeeded, message) = (false, $"Not run: {owner.Name} ({owner.LicenceModule}) is not licensed.");
        }
        else
        {
            try
            {
                Result<string> result = await task.RunAsync(job.CompanyId, now, ct).ConfigureAwait(false);
                (succeeded, message) = result.IsSuccess ? (true, result.Value) : (false, result.Error.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                (succeeded, message) = (false, $"{ex.GetType().Name}: {ex.Message}");
                _db.ChangeTracker.Clear(); // whatever the task half-did must not be saved with the run
            }
        }

        _db.Set<JobRun>().Add(new JobRun(job.Id, now, now.Add(DateTimeOffset.UtcNow - started), succeeded, message.Length > 2000 ? message[..2000] : message));
        return new JobRunEntry(now, succeeded, message);
    }

    // A task that threw cleared the tracker; the job is tracked again before its run is recorded.
    private void Track(ScheduledJob job)
    {
        if (_db.Entry(job).State == EntityState.Detached)
        {
            _db.Attach(job);
        }
    }

    private async Task<Result<ScheduledJob>> FindAsync(long companyId, string code, CancellationToken ct)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        ScheduledJob? job = await _db.Set<ScheduledJob>().FirstOrDefaultAsync(j => j.CompanyId == companyId && j.Code == key, ct).ConfigureAwait(false);
        if (job is null)
        {
            return Error.NotFound("Job.NotFound", $"Job {key} not found.");
        }

        return job;
    }
}

/// <summary>Built-in task: despatch queued messages (three attempts each).</summary>
public sealed class DespatchMessagesTask : IScheduledTask
{
    private readonly INotificationService _messages;

    /// <summary>Creates the task.</summary>
    /// <param name="messages">The notification service.</param>
    public DespatchMessagesTask(INotificationService messages) => _messages = messages;

    /// <inheritdoc />
    public string Key => "messages.despatch";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        DespatchRun run = await _messages.DespatchAsync(companyId, 3, ct).ConfigureAwait(false);
        return $"{run.Sent} sent, {run.Retrying} to retry, {run.Failed} failed.";
    }
}

/// <summary>Built-in task: the waiting list's yearly renewals - applicants who have not renewed are suspended, and removed after the grace period.</summary>
public sealed class WaitingListRenewalsTask : IScheduledTask
{
    private readonly Application.Abstractions.Housing.IWaitingListControlService _waitingList;

    /// <summary>Creates the task.</summary>
    /// <param name="waitingList">The waiting-list control service.</param>
    public WaitingListRenewalsTask(Application.Abstractions.Housing.IWaitingListControlService waitingList) => _waitingList = waitingList;

    /// <inheritdoc />
    public string Key => "housing.renewals";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        Application.Abstractions.Housing.RenewalRunResult run = await _waitingList.RunRenewalsAsync(companyId, DateOnly.FromDateTime(runAt.UtcDateTime), 12, 30, ct).ConfigureAwait(false);
        return $"{run.Reminded} applicant(s) reminded, {run.Suspended} suspended for not renewing, {run.Removed} removed after the grace period; {run.ByLetter} notice(s) to send by letter.";
    }
}

/// <summary>Built-in task: depreciate the fixed assets for the month just ended (a month already charged is skipped, so running it daily is harmless).</summary>
public sealed class MonthlyDepreciationTask : IScheduledTask
{
    private readonly Application.Abstractions.FixedAssets.IFixedAssetService _assets;

    /// <summary>Creates the task.</summary>
    /// <param name="assets">The fixed-asset service.</param>
    public MonthlyDepreciationTask(Application.Abstractions.FixedAssets.IFixedAssetService assets) => _assets = assets;

    /// <inheritdoc />
    public string Key => "assets.depreciation";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(runAt.UtcDateTime);
        DateOnly monthEnd = new DateOnly(today.Year, today.Month, 1).AddDays(-1);
        Result<Application.Abstractions.FixedAssets.DepreciationRunResult> run = await _assets.RunDepreciationAsync(companyId, monthEnd, $"DEP-{monthEnd:yyyyMM}", ct).ConfigureAwait(false);
        return run.IsFailure ? run.Error : $"{run.Value.AssetsDepreciated} asset(s) depreciated for {monthEnd:MMMM yyyy}, {run.Value.TotalDepreciation:N2} charged.";
    }
}

/// <summary>
/// Built-in task: true the IAS 19 leave provision up at the end of the month just ended - the leave staff are owed, at each one's daily rate,
/// against accounts 6600 (leave expense) and 2460 (leave provision).
/// </summary>
public sealed class LeaveProvisionTask : IScheduledTask
{
    private readonly Application.Abstractions.Payroll.ILeaveService _leave;

    /// <summary>Creates the task.</summary>
    /// <param name="leave">The leave service.</param>
    public LeaveProvisionTask(Application.Abstractions.Payroll.ILeaveService leave) => _leave = leave;

    /// <inheritdoc />
    public string Key => "payroll.leave-provision";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(runAt.UtcDateTime);
        DateOnly monthEnd = new DateOnly(today.Year, today.Month, 1).AddDays(-1);
        Result<decimal> provision = await _leave.RaiseLeaveProvisionAsync(companyId, monthEnd, 0m, "6600", "2460", $"LEAVE-PROV-{monthEnd:yyyyMM}", ct).ConfigureAwait(false);
        return provision.IsFailure ? provision.Error : $"Leave provision at {monthEnd:yyyy-MM-dd} trued up to {provision.Value:N2}.";
    }
}

/// <summary>Built-in task: the nightly sweep of the waiting list for applicants registered twice or reported deceased (each opens a case to review).</summary>
public sealed class ApplicantDuplicateScanTask : IScheduledTask
{
    private readonly Application.Abstractions.Housing.IApplicantIntakeService _intake;

    /// <summary>Creates the task.</summary>
    /// <param name="intake">The applicant intake service.</param>
    public ApplicantDuplicateScanTask(Application.Abstractions.Housing.IApplicantIntakeService intake) => _intake = intake;

    /// <inheritdoc />
    public string Key => "housing.duplicate-scan";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        Application.Abstractions.Housing.DuplicateScan scan = await _intake.ScanAsync(companyId, DateOnly.FromDateTime(runAt.UtcDateTime), ct).ConfigureAwait(false);
        return $"{scan.Opened} possible duplicate(s) and {scan.Deceased} deceased applicant(s) opened for review.";
    }
}

/// <summary>Built-in task: post every recurring journal that has fallen due (each template only once per due date).</summary>
public sealed class RecurringJournalsTask : IScheduledTask
{
    private readonly Application.Abstractions.Accounting.IRecurringJournalService _journals;

    /// <summary>Creates the task.</summary>
    /// <param name="journals">The recurring journal service.</param>
    public RecurringJournalsTask(Application.Abstractions.Accounting.IRecurringJournalService journals) => _journals = journals;

    /// <inheritdoc />
    public string Key => "gl.recurring-journals";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        Result<IReadOnlyList<Application.Abstractions.Accounting.RecurringRunLine>> run = await _journals.PostAllActiveAsync(companyId, DateOnly.FromDateTime(runAt.UtcDateTime), ct).ConfigureAwait(false);
        return run.IsFailure ? run.Error : $"{run.Value.Count(l => !l.AlreadyPosted)} recurring journal(s) posted.";
    }
}

/// <summary>Built-in task: raise the recurring customer and supplier invoices that have fallen due.</summary>
public sealed class RecurringInvoicesTask : IScheduledTask
{
    private readonly Application.Abstractions.Trading.IRecurringSalesInvoiceService _sales;
    private readonly Application.Abstractions.Trading.IRecurringPurchaseInvoiceService _purchases;

    /// <summary>Creates the task.</summary>
    /// <param name="sales">The recurring sales invoices.</param>
    /// <param name="purchases">The recurring supplier invoices.</param>
    public RecurringInvoicesTask(Application.Abstractions.Trading.IRecurringSalesInvoiceService sales, Application.Abstractions.Trading.IRecurringPurchaseInvoiceService purchases)
    {
        _sales = sales;
        _purchases = purchases;
    }

    /// <inheritdoc />
    public string Key => "trading.recurring-invoices";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(runAt.UtcDateTime);
        Result<int> sales = await _sales.GenerateDueAsync(companyId, today, ct).ConfigureAwait(false);
        if (sales.IsFailure)
        {
            return sales.Error;
        }

        Result<int> purchases = await _purchases.GenerateDueAsync(companyId, today, ct).ConfigureAwait(false);
        return purchases.IsFailure ? purchases.Error : $"{sales.Value} recurring customer invoice(s) and {purchases.Value} supplier invoice(s) raised.";
    }
}

/// <summary>Built-in task: pay or collect the standing orders and direct debits that have fallen due.</summary>
public sealed class StandingOrdersTask : IScheduledTask
{
    private readonly Application.Abstractions.Banking.IStandingOrderService _orders;

    /// <summary>Creates the task.</summary>
    /// <param name="orders">The standing order service.</param>
    public StandingOrdersTask(Application.Abstractions.Banking.IStandingOrderService orders) => _orders = orders;

    /// <inheritdoc />
    public string Key => "bank.standing-orders";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        Result<Application.Abstractions.Banking.RecurringBankRun> run = await _orders.RunDueAsync(companyId, DateOnly.FromDateTime(runAt.UtcDateTime), ct).ConfigureAwait(false);
        return run.IsFailure ? run.Error : $"{run.Value.Lines.Count} standing order(s) run, {run.Value.NothingOwed} with nothing owed.";
    }
}

/// <summary>Built-in task: raise the housing instalments due and bill the month's rent (both skip what is already raised).</summary>
public sealed class HousingBillingTask : IScheduledTask
{
    private readonly Application.Abstractions.Housing.IInstalmentSaleService _instalments;
    private readonly Application.Abstractions.Housing.IRentalHousingService _rentals;

    /// <summary>Creates the task.</summary>
    /// <param name="instalments">The instalment sale service.</param>
    /// <param name="rentals">The rental housing service.</param>
    public HousingBillingTask(Application.Abstractions.Housing.IInstalmentSaleService instalments, Application.Abstractions.Housing.IRentalHousingService rentals)
    {
        _instalments = instalments;
        _rentals = rentals;
    }

    /// <inheritdoc />
    public string Key => "housing.billing";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(runAt.UtcDateTime);
        Result<Application.Abstractions.Housing.InstalmentRunResult> instalments = await _instalments.RaiseDueAsync(companyId, today, ct).ConfigureAwait(false);
        if (instalments.IsFailure)
        {
            return instalments.Error;
        }

        Result<Application.Abstractions.Housing.RentRunResult> rent = await _rentals.RunRentAsync(companyId, new DateOnly(today.Year, today.Month, 1), ct).ConfigureAwait(false);
        return rent.IsFailure ? rent.Error
            : $"{instalments.Value.InstalmentsRaised} instalment(s) raised ({instalments.Value.InterestRaised:N2} interest); {rent.Value.TenanciesBilled} tenancy(ies) billed {rent.Value.TotalRent:N2} rent.";
    }
}

/// <summary>
/// Built-in task: tells the stock users (roles with an inventory right) and administrators which batches on hand have expired or expire
/// within 30 days (brief §4.2 expiry alerts), soonest first.
/// </summary>
public sealed class LotExpiryAlertsTask : IScheduledTask
{
    private readonly Persistence.One4AllDbContext _db;
    private readonly Application.Abstractions.Communications.INotificationService _notifications;

    /// <summary>Creates the task.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="notifications">Sends the in-app notifications.</param>
    public LotExpiryAlertsTask(Persistence.One4AllDbContext db, Application.Abstractions.Communications.INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    /// <inheritdoc />
    public string Key => "stock.expiry-alerts";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        DateOnly today = DateOnly.FromDateTime(runAt.UtcDateTime), horizon = today.AddDays(30);
        var lots = await _db.StockLotMovements.AsNoTracking().Where(l => l.CompanyId == companyId).GroupBy(l => new { l.ItemId, l.WarehouseId, l.LotNumber })
            .Select(g => new { g.Key.ItemId, g.Key.WarehouseId, g.Key.LotNumber, OnHand = g.Sum(l => l.Quantity), Expiry = g.Max(l => l.ExpiryDate) })
            .Where(l => l.OnHand > 0m && l.Expiry != null && l.Expiry <= horizon).ToListAsync(ct).ConfigureAwait(false);
        if (lots.Count == 0)
        {
            return "No batches on hand expire within 30 days.";
        }

        Dictionary<long, string> items = await _db.Items.AsNoTracking().Where(i => i.CompanyId == companyId).ToDictionaryAsync(i => i.Id, i => i.Code, ct).ConfigureAwait(false);
        Dictionary<long, string> warehouses = await _db.Warehouses.AsNoTracking().Where(w => w.CompanyId == companyId).ToDictionaryAsync(w => w.Id, w => w.Code, ct).ConfigureAwait(false);
        string body = string.Join(Environment.NewLine, lots.OrderBy(l => l.Expiry).Select(l =>
            $"{items.GetValueOrDefault(l.ItemId, "?")} batch {l.LotNumber} in {warehouses.GetValueOrDefault(l.WarehouseId, "?")}: {l.OnHand:0.###} {(l.Expiry < today ? "expired" : "expires")} {l.Expiry:yyyy-MM-dd}"));
        List<long> roles = await _db.Roles.Where(r => r.Permissions.Any(p => p.PermissionCode.StartsWith("Inventory."))).Select(r => r.Id).ToListAsync(ct).ConfigureAwait(false);
        List<long> users = await _db.Users.Where(u => u.IsActive && (u.IsSystemAdministrator || u.Roles.Any(r => roles.Contains(r.RoleId)))).Select(u => u.Id).ToListAsync(ct).ConfigureAwait(false);
        int sent = 0;
        foreach (long user in users)
        {
            if ((await _notifications.NotifyUserAsync(companyId, user, $"{lots.Count} batch(es) expiring", body, ct).ConfigureAwait(false)).IsSuccess)
            {
                sent++;
            }
        }

        return $"{lots.Count} batch(es) expired or expiring within 30 days; {sent} user(s) told.";
    }
}

/// <summary>Built-in task: lapse allocation offers not accepted by their deadline.</summary>
public sealed class LapseOffersTask : IScheduledTask
{
    private readonly IAllocationService _allocations;

    /// <summary>Creates the task.</summary>
    /// <param name="allocations">The allocation service.</param>
    public LapseOffersTask(IAllocationService allocations) => _allocations = allocations;

    /// <inheritdoc />
    public string Key => "housing.lapse-offers";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        // Brief §4.10.4: expired offers go to an exceptions queue for an officer to lapse or extend with a reason; the job reports the queue rather than deciding.
        IReadOnlyList<ExpiredOffer> expired = await _allocations.ExpiredOffersAsync(companyId, DateOnly.FromDateTime(runAt.UtcDateTime), ct).ConfigureAwait(false);
        return expired.Count == 0 ? "No expired offers." : $"{expired.Count} expired offer(s) await an officer's decision (lapse or extend): {string.Join(", ", expired.Select(e => e.CertificateNumber))}.";
    }
}
