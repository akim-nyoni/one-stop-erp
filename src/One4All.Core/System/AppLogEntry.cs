using One4All.Core.Common;

namespace One4All.Core.System;

/// <summary>
/// One application log line (<c>sys.app_log</c>, brief §2.2 "Serilog → rolling file + Postgres table"): what every host (desktop, server, scheduler)
/// logged at information level and above, so support reads one table instead of each machine's files.
/// </summary>
public sealed class AppLogEntry : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private AppLogEntry()
    {
        Level = string.Empty;
        Source = string.Empty;
        Message = string.Empty;
        Host = string.Empty;
        Machine = string.Empty;
    }

    /// <summary>Creates a log line.</summary>
    /// <param name="loggedOnUtc">When.</param>
    /// <param name="level">The level (Information, Warning, Error, Fatal).</param>
    /// <param name="source">The logger (the type that logged).</param>
    /// <param name="message">The rendered message.</param>
    /// <param name="exception">The exception, if any.</param>
    /// <param name="properties">The structured properties as JSON.</param>
    /// <param name="host">Which host logged (Desktop, Server).</param>
    /// <param name="machine">The machine.</param>
    public AppLogEntry(DateTimeOffset loggedOnUtc, string level, string source, string message, string? exception, string? properties, string host, string machine)
    {
        LoggedOnUtc = loggedOnUtc;
        Level = level;
        Source = source;
        Message = message;
        Exception = exception;
        Properties = properties;
        Host = host;
        Machine = machine;
    }

    /// <summary>When.</summary>
    public DateTimeOffset LoggedOnUtc { get; private set; }

    /// <summary>The level.</summary>
    public string Level { get; private set; }

    /// <summary>The logger (the type that logged).</summary>
    public string Source { get; private set; }

    /// <summary>The rendered message.</summary>
    public string Message { get; private set; }

    /// <summary>The exception, if any.</summary>
    public string? Exception { get; private set; }

    /// <summary>The structured properties as JSON.</summary>
    public string? Properties { get; private set; }

    /// <summary>Which host logged.</summary>
    public string Host { get; private set; }

    /// <summary>The machine.</summary>
    public string Machine { get; private set; }
}
