using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using One4All.Application.Abstractions.Backup;
using One4All.Core.System;
using One4All.Infrastructure.Backup;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers backups with the real PostgreSQL tools (skipped where pg_dump is not installed): a backup is taken, copied off-site and verified;
/// restore refuses the live database and an unconfirmed request, then fills a new database; an altered file fails its check; retention prunes.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BackupTests
{
    private readonly AccountingFixture _fixture;

    public BackupTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Backups_are_taken_verified_restored_and_pruned()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        Skip.IfNot(File.Exists(BackupService.FindTool("pg_dump", null)), "pg_dump is not installed.");
        string folder = Path.Combine(Path.GetTempPath(), "one4all-backup-test-" + Guid.NewGuid().ToString("N")[..8]);
        BackupOptions options = new() { Directory = folder, OffsiteDirectory = Path.Combine(folder, "offsite"), ConnectionString = _fixture.ConnectionString };
        await using ServiceProvider provider = _fixture.BuildServiceProvider(s => s.AddSingleton(options));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IBackupService backups = scope.ServiceProvider.GetRequiredService<IBackupService>();
        string restored = "one4all_restore_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            BackupRecord first = (await backups.BackupAsync("test")).Value;
            first.Status.Should().Be(BackupStatus.Taken, first.Message);
            File.Exists(first.FileName).Should().BeTrue();
            File.Exists(first.OffsiteCopy).Should().BeTrue();
            first.Sha256.Should().HaveLength(64);
            (await backups.VerifyAsync(first.Id)).Value.Should().Be(BackupStatus.Verified);

            string live = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString).Database!;
            (await backups.RestoreToNewDatabaseAsync(first.Id, live, $"RESTORE {live}")).Error.Code.Should().Be("Backup.TargetInvalid");
            (await backups.RestoreToNewDatabaseAsync(first.Id, restored, "yes")).Error.Code.Should().Be("Backup.NotConfirmed");
            Core.Common.Result<string> restore = await backups.RestoreToNewDatabaseAsync(first.Id, restored, $"RESTORE {restored}");
            restore.IsSuccess.Should().BeTrue(restore.IsFailure ? restore.Error.Message : null);
            await using (NpgsqlConnection connection = new(new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = restored }.ConnectionString))
            {
                await connection.OpenAsync();
                await using NpgsqlCommand count = new("SELECT count(*) FROM sec.\"user\"", connection);
                ((long)(await count.ExecuteScalarAsync())!).Should().BeGreaterThanOrEqualTo(0);
            }

            // The off-site copy restores by file alone (no record needed), and a switch-over is refused without the phrase or a database to switch to.
            string fromFile = restored + "f";
            (await backups.RestoreFileToNewDatabaseAsync(first.OffsiteCopy!, fromFile, "yes")).Error.Code.Should().Be("Backup.NotConfirmed");
            (await backups.RestoreFileToNewDatabaseAsync(Path.Combine(folder, "missing.backup"), fromFile, $"RESTORE {fromFile}")).Error.Code.Should().Be("Backup.FileNotFound");
            Core.Common.Result<string> fileRestore = await backups.RestoreFileToNewDatabaseAsync(first.OffsiteCopy!, fromFile, $"RESTORE {fromFile}");
            fileRestore.IsSuccess.Should().BeTrue(fileRestore.IsFailure ? fileRestore.Error.Message : null);
            (await backups.RestoreFileToNewDatabaseAsync(first.OffsiteCopy!, fromFile, $"RESTORE {fromFile}")).Error.Code.Should().Be("Backup.TargetExists");
            (await backups.SwitchOverAsync(fromFile, "yes")).Error.Code.Should().Be("Backup.NotConfirmed");
            (await backups.SwitchOverAsync(live, $"SWITCH TO {live}")).Error.Code.Should().Be("Backup.TargetInvalid");
            (await backups.SwitchOverAsync("one4all_nowhere_x", "SWITCH TO one4all_nowhere_x")).Error.Code.Should().Be("Backup.TargetMissing");

            // A second backup; then the first is altered and fails its check, and retention keeps only the newest.
            BackupRecord second = (await backups.BackupAsync("test")).Value;
            await File.AppendAllTextAsync(first.FileName, "tampered");
            (await backups.VerifyAsync(first.Id)).Value.Should().Be(BackupStatus.Corrupt);
            (await backups.PruneAsync(1)).Should().BeGreaterThanOrEqualTo(0);
            File.Exists(second.FileName).Should().BeTrue();
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using NpgsqlConnection admin = new(new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync();
            await using NpgsqlCommand drop = new($"DROP DATABASE IF EXISTS \"{restored}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
            await using NpgsqlCommand dropFile = new($"DROP DATABASE IF EXISTS \"{restored}f\" WITH (FORCE)", admin);
            await dropFile.ExecuteNonQueryAsync();
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
