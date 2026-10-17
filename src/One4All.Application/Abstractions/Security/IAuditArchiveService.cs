using One4All.Core.Common;

namespace One4All.Application.Abstractions.Security;

/// <summary>An archived year of the audit log.</summary>
/// <param name="Year">The year.</param>
/// <param name="TableName">Where its rows are kept (still queryable).</param>
/// <param name="RowCount">How many rows.</param>
/// <param name="ContentHash">The SHA-256 over its row hashes in order.</param>
/// <param name="AuthorisedByKey">The licence-holder key that authorised it.</param>
/// <param name="ArchivedOnUtc">When.</param>
public sealed record AuditArchiveEntry(int Year, string TableName, long RowCount, string ContentHash, string AuthorisedByKey, DateTimeOffset ArchivedOnUtc);

/// <summary>A year of the audit log by partition, archived or live.</summary>
/// <param name="Year">The year.</param>
/// <param name="Rows">Rows in it.</param>
/// <param name="Archived">Whether it has been archived.</param>
public sealed record AuditYear(int Year, long Rows, bool Archived);

/// <summary>
/// Retention of the audit log (brief §5.1): the log is partitioned by year; a closed year is archived - its partition detached into the
/// archive schema, never deleted - only with the licence holder's signed authorisation for that year, oldest first. The hash chain carries on
/// across the archived years.
/// </summary>
public interface IAuditArchiveService
{
    /// <summary>Archives a year of the audit log.</summary>
    /// <param name="year">The year (before the current one).</param>
    /// <param name="authorisationToken">The licence holder's <c>MA1.</c> authorisation for <c>audit.archive</c> of that year.</param>
    /// <param name="userId">Who runs it.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The archive, or why it was refused.</returns>
    Task<Result<AuditArchiveEntry>> ArchiveYearAsync(int year, string authorisationToken, long? userId, CancellationToken ct = default);

    /// <summary>The years in the log, live and archived.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The years, oldest first.</returns>
    Task<IReadOnlyList<AuditYear>> YearsAsync(CancellationToken ct = default);

    /// <summary>The archived years.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The archives, oldest first.</returns>
    Task<IReadOnlyList<AuditArchiveEntry>> ArchivesAsync(CancellationToken ct = default);
}
