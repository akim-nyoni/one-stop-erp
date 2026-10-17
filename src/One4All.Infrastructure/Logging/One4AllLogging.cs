using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace One4All.Infrastructure.Logging;

/// <summary>
/// The logging stack (brief §2.2): Serilog writing a daily rolling file and the <c>sys.app_log</c> table, with anything more (Seq, console) added
/// by a host's <c>Serilog</c> configuration section once its sink package is referenced.
/// </summary>
public static class One4AllLogging
{
    /// <summary>Where a host keeps its log files when it names none: the user's local One4All folder.</summary>
    public static string DefaultLogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "One4All", "logs");

    /// <summary>Builds the logger and makes it the logging provider.</summary>
    /// <param name="services">The services.</param>
    /// <param name="connectionString">The database the log table is in.</param>
    /// <param name="host">Which host this is (Desktop, Server).</param>
    /// <param name="configuration">The host's configuration (its <c>Serilog</c> section, if any, adds to or overrides what is set here).</param>
    /// <param name="logDirectory">Where the rolling file goes (default: the local One4All folder).</param>
    /// <returns>The services.</returns>
    public static IServiceCollection AddOne4AllLogging(this IServiceCollection services, string connectionString, string host, IConfiguration? configuration = null, string? logDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        string directory = logDirectory ?? DefaultLogDirectory;
        Directory.CreateDirectory(directory);
        PostgresLogSink table = new(connectionString, host);
        LoggerConfiguration configured = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.WithProperty("Host", host)
            .Enrich.WithProperty("Machine", Environment.MachineName)
            .WriteTo.File(Path.Combine(directory, $"one4all-{host.ToLowerInvariant()}-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 31, shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.Sink(table, LogEventLevel.Information);
        if (configuration is not null)
        {
            configured = configured.ReadFrom.Configuration(configuration);
        }

        Serilog.Core.Logger logger = configured.CreateLogger();
        Log.Logger = logger;
        services.AddSingleton(table);
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(logger, dispose: true);
        });
        return services;
    }
}
