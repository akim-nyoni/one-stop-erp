using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Scheduling;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Desktop.ViewModels;

/// <summary>A scheduled job (pre-formatted for display).</summary>
/// <param name="Code">The job.</param>
/// <param name="Task">The task it runs.</param>
/// <param name="Schedule">Its schedule, in words.</param>
/// <param name="NextRun">When it next runs (local time).</param>
/// <param name="LastRun">When it last ran, and how.</param>
/// <param name="IsEnabled">Whether it runs.</param>
/// <param name="LastFailed">Whether its last run failed (drives colouring).</param>
public sealed record JobVm(string Code, string Task, string Schedule, string NextRun, string LastRun, bool IsEnabled, bool LastFailed);

/// <summary>A run in a job's history (pre-formatted for display).</summary>
/// <param name="When">When it ran (local time).</param>
/// <param name="Outcome">Succeeded or Failed.</param>
/// <param name="Message">What it did, or why it failed.</param>
/// <param name="Failed">Whether it failed (drives colouring).</param>
public sealed record JobRunVm(string When, string Outcome, string Message, bool Failed);

/// <summary>
/// Backs the Scheduled Jobs screen (brief §4.17): the company's jobs with schedule, next and last run, run now, enable or
/// disable, and the selected job's history. Scheduling rules live in the tested <see cref="IJobScheduler"/>.
/// </summary>
public sealed partial class ScheduledJobsViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private long _companyId;

    /// <summary>Creates the view model.</summary>
    /// <param name="services">The application service provider (scopes are created per operation).</param>
    public ScheduledJobsViewModel(IServiceProvider services) => _services = services;

    /// <summary>The jobs.</summary>
    public ObservableCollection<JobVm> Jobs { get; } = [];

    /// <summary>The selected job's runs.</summary>
    public ObservableCollection<JobRunVm> Runs { get; } = [];

    /// <summary>The selected job.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunNowCommand), nameof(ToggleCommand))]
    private JobVm? _selected;

    /// <summary>A status line.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    private bool HasSelection => Selected is not null;

    /// <summary>Loads the jobs.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        if (_companyId == 0)
        {
            _companyId = await db.Companies.OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(true);
        }

        List<ScheduledJob> jobs = await db.Set<ScheduledJob>().AsNoTracking().Where(j => j.CompanyId == _companyId).OrderBy(j => j.Code).ToListAsync(ct).ConfigureAwait(true);
        string? keep = Selected?.Code;
        Jobs.Clear();
        foreach (ScheduledJob job in jobs)
        {
            Jobs.Add(new JobVm(job.Code, job.TaskKey, Describe(job), job.IsEnabled ? Local(job.NextRunAt) : "(disabled)",
                job.LastRunAt is DateTimeOffset last ? $"{Local(last)} — {(job.LastSucceeded == true ? "succeeded" : "failed")}" : "never",
                job.IsEnabled, job.LastSucceeded == false));
        }

        Selected = Jobs.FirstOrDefault(j => j.Code == keep);
        StatusMessage = $"{Jobs.Count} job(s); the server runs due jobs every minute.";
    }

    /// <summary>Runs the selected job now (its schedule is unchanged).</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    public async Task RunNowAsync(CancellationToken ct = default)
    {
        string code = Selected!.Code;
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        Result<JobRunEntry> run = await scope.ServiceProvider.GetRequiredService<IJobScheduler>().RunNowAsync(_companyId, code, DateTimeOffset.UtcNow, ct).ConfigureAwait(true);
        StatusMessage = run.IsFailure ? run.Error.Message : $"{code}: {(run.Value.Succeeded ? "succeeded" : "failed")} — {run.Value.Message}";
        await RefreshAsync(ct).ConfigureAwait(true);
    }

    /// <summary>Enables or disables the selected job.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    public async Task ToggleAsync(CancellationToken ct = default)
    {
        JobVm job = Selected!;
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        Result toggled = await scope.ServiceProvider.GetRequiredService<IJobScheduler>().SetEnabledAsync(_companyId, job.Code, !job.IsEnabled, DateTimeOffset.UtcNow, ct).ConfigureAwait(true);
        StatusMessage = toggled.IsFailure ? toggled.Error.Message : $"{job.Code} {(job.IsEnabled ? "disabled" : "enabled")}.";
        await RefreshAsync(ct).ConfigureAwait(true);
    }

    partial void OnSelectedChanged(JobVm? value) => _ = LoadRunsAsync(value?.Code);

    private async Task LoadRunsAsync(string? code)
    {
        Runs.Clear();
        if (code is null)
        {
            return;
        }

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        Result<IReadOnlyList<JobRunEntry>> history = await scope.ServiceProvider.GetRequiredService<IJobScheduler>().HistoryAsync(_companyId, code).ConfigureAwait(true);
        foreach (JobRunEntry run in history.IsSuccess ? history.Value.Take(50) : [])
        {
            Runs.Add(new JobRunVm(Local(run.StartedAt), run.Succeeded ? "Succeeded" : "Failed", run.Message, !run.Succeeded));
        }
    }

    private static string Describe(ScheduledJob job)
    {
        string time = job.TimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture);
        return job.Frequency switch
        {
            JobFrequency.Hourly => $"Hourly at :{job.TimeOfDay.Minute:00}",
            JobFrequency.Daily => $"Daily at {time} UTC",
            JobFrequency.Weekly => $"{job.DayOfWeek}s at {time} UTC",
            JobFrequency.Monthly => $"Monthly on day {job.DayOfMonth} at {time} UTC",
            _ => job.Frequency.ToString(),
        };
    }

    private static string Local(DateTimeOffset at) => at.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture);
}
