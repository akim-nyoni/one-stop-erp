using One4All.Application.Abstractions.Integration;

namespace One4All.Server;

/// <summary>Sends due webhook deliveries once a minute (brief §10); failed ones are retried with back-off by the service.</summary>
public sealed partial class WebhookHost : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WebhookHost> _logger;

    /// <summary>Creates the host.</summary>
    /// <param name="scopes">Creates a scope per pass.</param>
    /// <param name="logger">The logger.</param>
    public WebhookHost(IServiceScopeFactory scopes, ILogger<WebhookHost> logger)
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
                WebhookDispatchResult pass = await scope.ServiceProvider.GetRequiredService<IWebhookService>().DispatchDueAsync(DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false);
                if (pass.Delivered + pass.Retrying + pass.Failed > 0)
                {
                    LogPass(pass.Delivered, pass.Retrying, pass.Failed);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhooks: {Delivered} delivered, {Retrying} to retry, {Failed} given up")]
    private partial void LogPass(int delivered, int retrying, int failed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Webhook dispatch failed")]
    private partial void LogFailed(Exception ex);
}
