using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Npgsql;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace One4All.Infrastructure.Logging;

/// <summary>
/// Writes log events to <c>sys.app_log</c> in batches from a background drain, never blocking the caller and never failing the application: a
/// database that is down or not yet migrated loses log lines (noted in Serilog's self-log), nothing else.
/// </summary>
public sealed class PostgresLogSink : ILogEventSink, IDisposable
{
    private const int BatchSize = 100;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private readonly string _connectionString;
    private readonly string _host;
    private readonly string _machine = Environment.MachineName;
    private readonly Channel<LogEvent> _queue = Channel.CreateBounded<LogEvent>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _drain;
    private bool _disposed;

    /// <summary>Creates the sink.</summary>
    /// <param name="connectionString">The database.</param>
    /// <param name="host">Which host logs (Desktop, Server).</param>
    public PostgresLogSink(string connectionString, string host)
    {
        _connectionString = connectionString;
        _host = host;
        _drain = Task.Run(DrainAsync);
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent) => _queue.Writer.TryWrite(logEvent);

    /// <summary>Writes what is queued now (used when a host shuts down, and by tests).</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of lines written.</returns>
    public async Task<int> FlushAsync(CancellationToken ct = default)
    {
        List<LogEvent> batch = [];
        while (batch.Count < 10_000 && _queue.Reader.TryRead(out LogEvent? item))
        {
            batch.Add(item);
        }

        return batch.Count == 0 ? 0 : await WriteAsync(batch, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Serilog disposes the sink with the logger and the host's container disposes the singleton: the second call is a no-op.
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.Writer.TryComplete();
        _stopping.Cancel();
        try
        {
            _drain.Wait(TimeSpan.FromSeconds(5));
            FlushAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is AggregateException or OperationCanceledException or NpgsqlException or TimeoutException)
        {
            // Shutting down: what could not be written is lost, as the file sink still has it.
        }

        _stopping.Dispose();
    }

    private async Task DrainAsync()
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(_stopping.Token).ConfigureAwait(false))
            {
                await Task.Delay(Interval, _stopping.Token).ConfigureAwait(false);
                List<LogEvent> batch = [];
                while (batch.Count < BatchSize && _queue.Reader.TryRead(out LogEvent? item))
                {
                    batch.Add(item);
                }

                if (batch.Count > 0)
                {
                    await WriteAsync(batch, _stopping.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }

    private async Task<int> WriteAsync(IReadOnlyList<LogEvent> batch, CancellationToken ct)
    {
        try
        {
            await using NpgsqlConnection connection = new(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using NpgsqlBatch commands = new(connection);
            foreach (LogEvent e in batch)
            {
                NpgsqlBatchCommand command = new("INSERT INTO sys.app_log (logged_on_utc, level, source, message, exception, properties, host, machine) VALUES ($1, $2, $3, $4, $5, $6, $7, $8)");
                command.Parameters.Add(new NpgsqlParameter { Value = e.Timestamp.ToUniversalTime() });
                command.Parameters.Add(new NpgsqlParameter { Value = e.Level.ToString() });
                command.Parameters.Add(new NpgsqlParameter { Value = Source(e) });
                command.Parameters.Add(new NpgsqlParameter { Value = e.RenderMessage(CultureInfo.InvariantCulture) });
                command.Parameters.Add(new NpgsqlParameter { Value = (object?)e.Exception?.ToString() ?? DBNull.Value });
                command.Parameters.Add(new NpgsqlParameter { Value = (object?)Properties(e) ?? DBNull.Value });
                command.Parameters.Add(new NpgsqlParameter { Value = _host });
                command.Parameters.Add(new NpgsqlParameter { Value = _machine });
                commands.BatchCommands.Add(command);
            }

            await commands.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return batch.Count;
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or TimeoutException or System.Net.Sockets.SocketException)
        {
            SelfLog.WriteLine("One4All Postgres log sink: {0} line(s) not written: {1}", batch.Count, ex.Message);
            return 0;
        }
    }

    private static string Source(LogEvent e)
    {
        string source = e.Properties.TryGetValue("SourceContext", out LogEventPropertyValue? value) && value is ScalarValue { Value: string s } ? s : string.Empty;
        return source.Length > 200 ? source[^200..] : source;
    }

    private static string? Properties(LogEvent e)
    {
        Dictionary<string, object?> values = [];
        foreach ((string name, LogEventPropertyValue value) in e.Properties)
        {
            if (name is "SourceContext" or "Host" or "Machine")
            {
                continue;
            }

            values[name] = value is ScalarValue scalar ? scalar.Value : value.ToString();
        }

        return values.Count == 0 ? null : JsonSerializer.Serialize(values);
    }
}
