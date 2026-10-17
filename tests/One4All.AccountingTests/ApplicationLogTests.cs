using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using One4All.Core.System;
using One4All.Infrastructure.Logging;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the logging stack (brief §2.2, ADR-0007): what a host logs through ILogger lands in the daily file and, batched, in sys.app_log with its
/// level, source, message, exception and host, and a database that cannot be reached loses the line without failing the caller.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ApplicationLogTests
{
    private readonly AccountingFixture _fixture;

    public ApplicationLogTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Log_lines_reach_the_file_and_the_table()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        string directory = Path.Combine(Path.GetTempPath(), "one4all-log-test-" + Guid.NewGuid().ToString("N"));
        await using ServiceProvider provider = _fixture.BuildServiceProvider(services => services.AddOne4AllLogging(_fixture.ConnectionString, "Test", logDirectory: directory));
        try
        {
            string marker = "LOGTEST-" + Guid.NewGuid().ToString("N");
            ILogger<ApplicationLogTests> logger = provider.GetRequiredService<ILogger<ApplicationLogTests>>();
            logger.LogInformation("Marker {Marker} logged for {Company}", marker, "Test Trading Co");
            logger.LogWarning(new InvalidOperationException("the drawer is open"), "Warning with {Marker}", marker);
            logger.LogDebug("Debug noise {Marker}", marker);

            PostgresLogSink sink = provider.GetRequiredService<PostgresLogSink>();
            (await sink.FlushAsync()).Should().BeGreaterThanOrEqualTo(2, "information and warning go to the table, debug does not");

            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            List<AppLogEntry> lines = await db.AppLog.AsNoTracking().Where(l => l.Message.Contains(marker)).OrderBy(l => l.Id).ToListAsync();
            lines.Should().HaveCount(2);
            (lines[0].Level, lines[0].Host, lines[0].Machine, lines[0].Source).Should().Be(("Information", "Test", Environment.MachineName, typeof(ApplicationLogTests).FullName!));
            lines[0].Message.Should().Be($"Marker \"{marker}\" logged for \"Test Trading Co\"");
            lines[0].Properties.Should().Contain("\"Company\":\"Test Trading Co\"");
            lines[1].Level.Should().Be("Warning");
            lines[1].Exception.Should().Contain("the drawer is open");

            string[] files = Directory.GetFiles(directory, "one4all-test-*.log");
            files.Should().ContainSingle();
            Serilog.Log.CloseAndFlush();
            (await File.ReadAllTextAsync(files[0])).Should().Contain(marker).And.Contain("[WRN]");
        }
        finally
        {
            Serilog.Log.CloseAndFlush();
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // The file may still be held briefly by the sink; the temp folder is cleaned up by the OS.
            }
        }
    }

    [Fact]
    public async Task A_database_that_cannot_be_reached_loses_the_line_quietly()
    {
        using PostgresLogSink sink = new("Host=127.0.0.1;Port=1;Database=none;Username=x;Password=y;Timeout=1", "Test");
        sink.Emit(new Serilog.Events.LogEvent(DateTimeOffset.UtcNow, Serilog.Events.LogEventLevel.Error, null, new Serilog.Events.MessageTemplate("lost", []), []));
        (await sink.FlushAsync()).Should().Be(0, "the line is dropped and noted in Serilog's self-log, nothing throws");
    }
}
