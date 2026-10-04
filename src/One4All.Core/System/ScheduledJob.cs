using One4All.Core.Common;

namespace One4All.Core.System;

/// <summary>How often a scheduled job runs.</summary>
public enum JobFrequency
{
    /// <summary>Every hour.</summary>
    Hourly = 1,

    /// <summary>Every day.</summary>
    Daily = 2,

    /// <summary>Every week.</summary>
    Weekly = 3,

    /// <summary>Every month.</summary>
    Monthly = 4,
}

/// <summary>
/// A scheduled job (<c>sys.scheduled_job</c>, brief §4.17): which registered task runs for a company, on what schedule,
/// when it next runs, and how its last run went.
/// </summary>
public sealed class ScheduledJob : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ScheduledJob()
    {
        Code = string.Empty;
        TaskKey = string.Empty;
    }

    /// <summary>Defines a job.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The job code.</param>
    /// <param name="taskKey">The registered task it runs.</param>
    /// <param name="frequency">How often.</param>
    /// <param name="timeOfDay">At what time (UTC).</param>
    /// <param name="dayOfWeek">On which day (weekly).</param>
    /// <param name="dayOfMonth">On which day (monthly).</param>
    /// <param name="nextRunAt">The first run.</param>
    public ScheduledJob(long companyId, string code, string taskKey, JobFrequency frequency, TimeOnly timeOfDay, DayOfWeek? dayOfWeek, int? dayOfMonth, DateTimeOffset nextRunAt)
    {
        CompanyId = companyId;
        Code = code;
        TaskKey = taskKey;
        Frequency = frequency;
        TimeOfDay = timeOfDay;
        DayOfWeek = dayOfWeek;
        DayOfMonth = dayOfMonth;
        NextRunAt = nextRunAt;
        IsEnabled = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The job code.</summary>
    public string Code { get; private set; }

    /// <summary>The registered task it runs.</summary>
    public string TaskKey { get; private set; }

    /// <summary>How often.</summary>
    public JobFrequency Frequency { get; private set; }

    /// <summary>At what time (UTC).</summary>
    public TimeOnly TimeOfDay { get; private set; }

    /// <summary>On which day (weekly).</summary>
    public DayOfWeek? DayOfWeek { get; private set; }

    /// <summary>On which day (monthly).</summary>
    public int? DayOfMonth { get; private set; }

    /// <summary>Whether it runs.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>When it next runs.</summary>
    public DateTimeOffset NextRunAt { get; private set; }

    /// <summary>When it last ran.</summary>
    public DateTimeOffset? LastRunAt { get; private set; }

    /// <summary>Whether its last run succeeded.</summary>
    public bool? LastSucceeded { get; private set; }

    /// <summary>Enables or disables it.</summary>
    /// <param name="enabled">Whether it runs.</param>
    /// <param name="nextRunAt">When it next runs, if enabled.</param>
    public void SetEnabled(bool enabled, DateTimeOffset nextRunAt)
    {
        IsEnabled = enabled;
        NextRunAt = nextRunAt;
    }

    /// <summary>Records a run.</summary>
    /// <param name="at">When it ran.</param>
    /// <param name="succeeded">Whether it succeeded.</param>
    /// <param name="nextRunAt">When it next runs.</param>
    public void Ran(DateTimeOffset at, bool succeeded, DateTimeOffset nextRunAt)
    {
        LastRunAt = at;
        LastSucceeded = succeeded;
        NextRunAt = nextRunAt;
    }
}

/// <summary>One run of a scheduled job (<c>sys.job_run</c>).</summary>
public sealed class JobRun : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private JobRun()
    {
        Message = string.Empty;
    }

    /// <summary>Records a run.</summary>
    /// <param name="scheduledJobId">The job.</param>
    /// <param name="startedAt">When it started.</param>
    /// <param name="finishedAt">When it finished.</param>
    /// <param name="succeeded">Whether it succeeded.</param>
    /// <param name="message">What it did, or why it failed.</param>
    public JobRun(long scheduledJobId, DateTimeOffset startedAt, DateTimeOffset finishedAt, bool succeeded, string message)
    {
        ScheduledJobId = scheduledJobId;
        StartedAt = startedAt;
        FinishedAt = finishedAt;
        Succeeded = succeeded;
        Message = message;
    }

    /// <summary>The job.</summary>
    public long ScheduledJobId { get; private set; }

    /// <summary>When it started.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>When it finished.</summary>
    public DateTimeOffset FinishedAt { get; private set; }

    /// <summary>Whether it succeeded.</summary>
    public bool Succeeded { get; private set; }

    /// <summary>What it did, or why it failed.</summary>
    public string Message { get; private set; }
}
