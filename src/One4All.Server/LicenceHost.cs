using One4All.Application.Abstractions.Licensing;

namespace One4All.Server;

/// <summary>
/// Re-validates the installed licence every few hours (brief §11.5), so expiry, grace and read-only take effect without a restart and a
/// licence installed on another node is picked up.
/// </summary>
public sealed partial class LicenceHost : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly ILicenceService _licence;
    private readonly ILogger<LicenceHost> _logger;

    /// <summary>Creates the host.</summary>
    /// <param name="licence">The licence service.</param>
    /// <param name="logger">The logger.</param>
    public LicenceHost(ILicenceService licence, ILogger<LicenceHost> logger)
    {
        _licence = licence;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await _licence.ReloadAsync(stoppingToken).ConfigureAwait(false);
                if (_licence.Warning is string warning)
                {
                    LogWarning(warning);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Licence: {Warning}")]
    private partial void LogWarning(string warning);

    [LoggerMessage(Level = LogLevel.Error, Message = "Licence re-validation failed")]
    private partial void LogFailed(Exception ex);
}
