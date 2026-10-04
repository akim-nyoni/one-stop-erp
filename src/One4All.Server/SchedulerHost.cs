using One4All.Application.Abstractions.Scheduling;

namespace One4All.Server;

/// <summary>
/// Runs due scheduled jobs (brief §4.17) once a minute in the server process, each pass in its own scope so jobs never
/// share a unit of work with requests. A failing pass is logged and the next one tries again.
/// </summary>
public sealed partial class SchedulerHost : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SchedulerHost> _logger;

    /// <summary>Creates the host.</summary>
    /// <param name="scopes">Creates a scope per pass.</param>
    /// <param name="logger">The logger.</param>
    public SchedulerHost(IServiceScopeFactory scopes, ILogger<SchedulerHost> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval);
        do
        {
            try
            {
                await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
                SchedulerPass pass = await scope.ServiceProvider.GetRequiredService<IJobScheduler>().RunDueAsync(DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false);
                if (pass.Ran > 0)
                {
                    LogPass(pass.Ran, pass.Failed);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailure(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduler ran {Ran} job(s), {Failed} failed.")]
    private partial void LogPass(int ran, int failed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Scheduler pass failed.")]
    private partial void LogFailure(Exception ex);
}
