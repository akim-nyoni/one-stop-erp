using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using One4All.Application.Abstractions.Backup;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Backup;

/// <summary>Backup settings (configuration section <c>Backup</c>).</summary>
public sealed class BackupOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Backup";

    /// <summary>Gets or sets where backups are written (default: LocalApplicationData/One4All/Backups).</summary>
    public string? Directory { get; set; }

    /// <summary>Gets or sets a second place each backup is copied to (the off-site hook: a mapped drive, synced folder…).</summary>
    public string? OffsiteDirectory { get; set; }

    /// <summary>Gets or sets the folder holding pg_dump and pg_restore (default: on the PATH, else the newest PostgreSQL install).</summary>
    public string? BinDirectory { get; set; }

    /// <summary>Gets or sets how many good backups the scheduled task keeps.</summary>
    public int RetainCount { get; set; } = 14;

    /// <summary>Gets or sets the database connection (set by the host from the application's connection string).</summary>
    public string ConnectionString { get; set; } = string.Empty;
}

/// <summary>Database backup, integrity check, retention and restore into a new database (brief §9).</summary>
public sealed partial class BackupService : IBackupService
{
    private readonly One4AllDbContext _db;
    private readonly BackupOptions _options;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="options">The settings.</param>
    public BackupService(One4AllDbContext db, BackupOptions options)
    {
        _db = db;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<Result<BackupRecord>> BackupAsync(string reason, CancellationToken ct = default)
    {
        NpgsqlConnectionStringBuilder connection = new(_options.ConnectionString);
        string folder = _options.Directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "One4All", "Backups");
        Directory.CreateDirectory(folder);
        DateTimeOffset started = DateTimeOffset.UtcNow;
        string file = Path.Combine(folder, $"{connection.Database}-{started:yyyyMMdd-HHmmss}.dump");
        BackupRecord record = new(file, string.IsNullOrWhiteSpace(reason) ? "manual" : reason.Trim(), started);
        _db.Add(record);

        (int exit, string error) = await RunAsync("pg_dump", ["--format=custom", "--no-password", $"--file={file}", .. ConnectionArgs(connection), connection.Database!], connection, ct).ConfigureAwait(false);
        if (exit != 0 || !File.Exists(file))
        {
            record.Mark(BackupStatus.Failed, $"pg_dump failed ({exit}): {Trim(error)}");
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return record;
        }

        string hash = await HashAsync(file, ct).ConfigureAwait(false);
        string? offsite = null;
        if (!string.IsNullOrWhiteSpace(_options.OffsiteDirectory))
        {
            Directory.CreateDirectory(_options.OffsiteDirectory);
            offsite = Path.Combine(_options.OffsiteDirectory, Path.GetFileName(file));
            File.Copy(file, offsite, overwrite: false);
        }

        record.Complete(DateTimeOffset.UtcNow, new FileInfo(file).Length, hash, offsite);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return record;
    }

    /// <inheritdoc />
    public async Task<Result<BackupStatus>> VerifyAsync(long backupId, CancellationToken ct = default)
    {
        BackupRecord? record = await _db.Set<BackupRecord>().FirstOrDefaultAsync(b => b.Id == backupId, ct).ConfigureAwait(false);
        if (record is null || record.Status is BackupStatus.Failed or BackupStatus.Pruned)
        {
            return Error.NotFound("Backup.NotFound", $"No backup {backupId} with a file to check.");
        }

        string? problem = !File.Exists(record.FileName) ? "The file is missing."
            : !string.Equals(await HashAsync(record.FileName, ct).ConfigureAwait(false), record.Sha256, StringComparison.OrdinalIgnoreCase) ? "The file does not match its recorded hash."
            : null;
        if (problem is null)
        {
            (int exit, string error) = await RunAsync("pg_restore", ["--list", record.FileName], new NpgsqlConnectionStringBuilder(_options.ConnectionString), ct).ConfigureAwait(false);
            problem = exit == 0 ? null : $"The archive does not read back: {Trim(error)}";
        }

        record.Mark(problem is null ? BackupStatus.Verified : BackupStatus.Corrupt, problem ?? $"Verified {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC.");
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return record.Status;
    }

    /// <inheritdoc />
    public async Task<int> PruneAsync(int keep, CancellationToken ct = default)
    {
        List<BackupRecord> good = await _db.Set<BackupRecord>().Where(b => b.Status == BackupStatus.Taken || b.Status == BackupStatus.Verified).OrderByDescending(b => b.StartedOnUtc)
            .ToListAsync(ct).ConfigureAwait(false);
        List<BackupRecord> old = [.. good.Skip(Math.Max(1, keep))];
        foreach (BackupRecord record in old)
        {
            if (File.Exists(record.FileName))
            {
                File.Delete(record.FileName); // our own retention: off-site copies are left for the off-site policy
            }

            record.Mark(BackupStatus.Pruned, $"Pruned {DateTimeOffset.UtcNow:yyyy-MM-dd} under the keep-{keep} policy.");
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return old.Count;
    }

    /// <inheritdoc />
    public async Task<Result<string>> RestoreToNewDatabaseAsync(long backupId, string databaseName, string confirmation, CancellationToken ct = default)
    {
        NpgsqlConnectionStringBuilder live = new(_options.ConnectionString);
        string name = (databaseName ?? string.Empty).Trim();
        if (!DatabaseName().IsMatch(name) || string.Equals(name, live.Database, StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation("Backup.TargetInvalid", "Restore into a new database named with lower-case letters, digits and underscores — never the live one.");
        }

        if (!string.Equals(confirmation?.Trim(), $"RESTORE {name}", StringComparison.Ordinal))
        {
            return Error.Validation("Backup.NotConfirmed", $"Type RESTORE {name} to confirm.");
        }

        Result<BackupStatus> verified = await VerifyAsync(backupId, ct).ConfigureAwait(false);
        if (verified.IsFailure || verified.Value != BackupStatus.Verified)
        {
            return Error.Rule("Backup.NotVerified", "Only a backup that passes its check can be restored.");
        }

        NpgsqlConnectionStringBuilder maintenance = new(_options.ConnectionString) { Database = "postgres" };
        await using (NpgsqlConnection connection = new(maintenance.ConnectionString))
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using NpgsqlCommand exists = new("SELECT 1 FROM pg_database WHERE datname = @n", connection);
            exists.Parameters.AddWithValue("n", name);
            if (await exists.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null)
            {
                return Error.Conflict("Backup.TargetExists", $"Database {name} already exists.");
            }

            await using NpgsqlCommand create = new($"CREATE DATABASE \"{name}\"", connection); // the name is validated above
            await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        BackupRecord record = await _db.Set<BackupRecord>().AsNoTracking().SingleAsync(b => b.Id == backupId, ct).ConfigureAwait(false);
        NpgsqlConnectionStringBuilder target = new(_options.ConnectionString) { Database = name };
        (int exit, string error) = await RunAsync("pg_restore", ["--no-owner", "--no-password", .. ConnectionArgs(target), $"--dbname={name}", record.FileName], target, ct).ConfigureAwait(false);
        return exit == 0 ? $"Restored backup {backupId} into {name}." : Error.Failure("Backup.RestoreFailed", $"pg_restore failed ({exit}): {Trim(error)}");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BackupRecord>> ListAsync(int take = 100, CancellationToken ct = default)
        => await _db.Set<BackupRecord>().AsNoTracking().OrderByDescending(b => b.Id).Take(take).ToListAsync(ct).ConfigureAwait(false);

    /// <summary>Finds a PostgreSQL client tool: in the configured folder, on the PATH, or in the newest PostgreSQL install.</summary>
    /// <param name="tool">The tool (pg_dump, pg_restore).</param>
    /// <param name="binDirectory">The configured folder.</param>
    /// <returns>The path to run.</returns>
    public static string FindTool(string tool, string? binDirectory)
    {
        string exe = OperatingSystem.IsWindows() ? tool + ".exe" : tool;
        if (!string.IsNullOrWhiteSpace(binDirectory))
        {
            return Path.Combine(binDirectory, exe);
        }

        string? onPath = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator).Select(d => Path.Combine(d.Trim(), exe)).FirstOrDefault(File.Exists);
        if (onPath is not null)
        {
            return onPath;
        }

        string installs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PostgreSQL");
        return Directory.Exists(installs)
            ? Directory.GetDirectories(installs).Select(d => Path.Combine(d, "bin", exe)).Where(File.Exists).OrderByDescending(p => p, StringComparer.Ordinal).FirstOrDefault() ?? exe
            : exe;
    }

    private static IEnumerable<string> ConnectionArgs(NpgsqlConnectionStringBuilder connection)
        => [$"--host={connection.Host}", $"--port={connection.Port}", $"--username={connection.Username}"];

    private static string Trim(string text) => text.Length > 400 ? text[..400] : text.Trim();

    private static async Task<string> HashAsync(string file, CancellationToken ct)
    {
        await using FileStream stream = File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }

    // The password reaches the tool through its environment, never the command line or a file.
    private async Task<(int Exit, string Error)> RunAsync(string tool, IReadOnlyList<string> arguments, NpgsqlConnectionStringBuilder connection, CancellationToken ct)
    {
        ProcessStartInfo start = new(FindTool(tool, _options.BinDirectory)) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrEmpty(connection.Password))
        {
            start.Environment["PGPASSWORD"] = connection.Password;
        }

        try
        {
            using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{tool} did not start.");
            Task<string> output = process.StandardOutput.ReadToEndAsync(ct);
            string error = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await output.ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return (process.ExitCode, error);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return (-1, $"{tool} could not be run ({ex.Message}); set Backup:BinDirectory.");
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{2,40}$")]
    private static partial Regex DatabaseName();
}

/// <summary>Scheduled task: take a backup, check it, and keep the newest per the retention setting (key <c>system.backup</c>).</summary>
public sealed class BackupTask : Application.Abstractions.Scheduling.IScheduledTask
{
    private readonly IBackupService _backups;
    private readonly BackupOptions _options;

    /// <summary>Creates the task.</summary>
    /// <param name="backups">The backup service.</param>
    /// <param name="options">The settings.</param>
    public BackupTask(IBackupService backups, BackupOptions options)
    {
        _backups = backups;
        _options = options;
    }

    /// <inheritdoc />
    public string Key => "system.backup";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        Result<BackupRecord> taken = await _backups.BackupAsync("scheduled", ct).ConfigureAwait(false);
        if (taken.IsFailure || taken.Value.Status == BackupStatus.Failed)
        {
            return Error.Failure("Backup.Failed", taken.IsFailure ? taken.Error.Message : taken.Value.Message ?? "Backup failed.");
        }

        Result<BackupStatus> check = await _backups.VerifyAsync(taken.Value.Id, ct).ConfigureAwait(false);
        int pruned = await _backups.PruneAsync(_options.RetainCount, ct).ConfigureAwait(false);
        return check.IsSuccess && check.Value == BackupStatus.Verified
            ? $"Backup {Path.GetFileName(taken.Value.FileName)} taken and verified ({taken.Value.SizeBytes:N0} bytes); {pruned} old backup(s) pruned."
            : Error.Failure("Backup.Corrupt", $"Backup {taken.Value.Id} did not verify.");
    }
}

/// <summary>Scheduled task: the nightly reconciliation of budget commitments to purchase orders (key <c>budget.reconcile-commitments</c>).</summary>
public sealed class ReconcileCommitmentsTask : Application.Abstractions.Scheduling.IScheduledTask
{
    private readonly Application.Abstractions.Budgeting.IBudgetGateService _budget;

    /// <summary>Creates the task.</summary>
    /// <param name="budget">The budget gate.</param>
    public ReconcileCommitmentsTask(Application.Abstractions.Budgeting.IBudgetGateService budget) => _budget = budget;

    /// <inheritdoc />
    public string Key => "budget.reconcile-commitments";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        IReadOnlyList<Application.Abstractions.Budgeting.CommitmentException> exceptions = await _budget.ReconcileAsync(companyId, ct).ConfigureAwait(false);
        return exceptions.Count == 0 ? "Commitments agree with orders."
            : $"{exceptions.Count} exception(s): {string.Join("; ", exceptions.Take(10).Select(e => $"{e.Reference} {e.AccountCode} {e.Problem}"))}";
    }
}
