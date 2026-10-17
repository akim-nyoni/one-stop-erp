using One4All.Core.Common;
using One4All.Core.System;

namespace One4All.Application.Abstractions.Backup;

/// <summary>
/// Database backup and restore (brief §9 System Manager utility): pg_dump to a custom-format archive with its SHA-256 recorded, an
/// integrity check (hash plus a pg_restore read-back), an off-site copy hook, retention, and restore into a new database only - never
/// over the live one, and only with the confirmation phrase.
/// </summary>
public interface IBackupService
{
    /// <summary>Takes a backup now.</summary>
    /// <param name="reason">Why (scheduled, before upgrade…).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The backup record (Failed with the reason when pg_dump fails).</returns>
    Task<Result<BackupRecord>> BackupAsync(string reason, CancellationToken ct = default);

    /// <summary>Checks a backup: the file exists, matches its hash and reads back as an archive.</summary>
    /// <param name="backupId">The backup.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The status after the check.</returns>
    Task<Result<BackupStatus>> VerifyAsync(long backupId, CancellationToken ct = default);

    /// <summary>Removes the files of good backups beyond the newest <paramref name="keep"/>, marking them pruned.</summary>
    /// <param name="keep">How many to keep.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many were pruned.</returns>
    Task<int> PruneAsync(int keep, CancellationToken ct = default);

    /// <summary>
    /// Restores a verified backup into a new database (lower-case letters, digits and underscores; must not exist and is never the live
    /// one). <paramref name="confirmation"/> must read <c>RESTORE {databaseName}</c>.
    /// </summary>
    /// <param name="backupId">The backup.</param>
    /// <param name="databaseName">The new database.</param>
    /// <param name="confirmation">The confirmation phrase.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What happened.</returns>
    Task<Result<string>> RestoreToNewDatabaseAsync(long backupId, string databaseName, string confirmation, CancellationToken ct = default);

    /// <summary>
    /// Restores a backup file - an off-site copy, or one taken on another server, with no record in this database - into a new database
    /// (brief §10 disaster recovery). The archive is read back first (pg_restore --list); <paramref name="confirmation"/> must read
    /// <c>RESTORE {databaseName}</c>.
    /// </summary>
    /// <param name="filePath">The backup archive (pg_dump custom format).</param>
    /// <param name="databaseName">The new database.</param>
    /// <param name="confirmation">The confirmation phrase.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What happened.</returns>
    Task<Result<string>> RestoreFileToNewDatabaseAsync(string filePath, string databaseName, string confirmation, CancellationToken ct = default);

    /// <summary>
    /// Switches the system over to a restored database: every session on the live and restored databases is ended, the live database is
    /// renamed <c>{live}_retired_{yyyyMMddHHmm}</c> (kept, never dropped) and the restored one takes the live name, so the next start-up of
    /// the server and every desktop runs on it. <paramref name="confirmation"/> must read <c>SWITCH TO {databaseName}</c>.
    /// </summary>
    /// <param name="databaseName">The restored database that becomes live.</param>
    /// <param name="confirmation">The confirmation phrase.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What happened (restart everything afterwards).</returns>
    Task<Result<string>> SwitchOverAsync(string databaseName, string confirmation, CancellationToken ct = default);

    /// <summary>The backups, newest first.</summary>
    /// <param name="take">At most this many.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The records.</returns>
    Task<IReadOnlyList<BackupRecord>> ListAsync(int take = 100, CancellationToken ct = default);
}
