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

    /// <summary>Creates the scheduler.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="tasks">The registered tasks.</param>
    public JobScheduler(One4AllDbContext db, IEnumerable<IScheduledTask> tasks)
    {
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
        List<ScheduledJob> due = await _db.Set<ScheduledJob>().IgnoreQueryFilters().Where(j => j.IsEnabled && j.NextRunAt <= now && (companyId == null || j.CompanyId == companyId))
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
        IReadOnlyList<string> lapsed = await _allocations.LapseExpiredAsync(companyId, DateOnly.FromDateTime(runAt.UtcDateTime), ct).ConfigureAwait(false);
        return lapsed.Count == 0 ? "No offers lapsed." : $"{lapsed.Count} offers lapsed: {string.Join(", ", lapsed)}.";
    }
}
