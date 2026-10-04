using One4All.Application.Reporting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Reporting;

/// <summary>A version in a report's history.</summary>
/// <param name="Version">The version number.</param>
/// <param name="SavedAt">When it was saved.</param>
/// <param name="SavedByUserId">Who saved it.</param>
/// <param name="Note">Why.</param>
/// <param name="IsCurrent">Whether it is the version in use.</param>
public sealed record ReportVersionEntry(int Version, DateTimeOffset SavedAt, long? SavedByUserId, string Note, bool IsCurrent);

/// <summary>
/// Report definition versioning (brief §13.2): system reports are read-only and are copied to a company version to be
/// changed; every save of a company report keeps the version it replaces; any two versions can be diffed; and any version
/// can be rolled back to, which saves it again as the newest version so nothing is ever lost.
/// </summary>
public interface IReportDefinitionService
{
    /// <summary>Copies a system report to a company version (the company's runs use it from then on).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The system report.</param>
    /// <param name="userId">Who.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> CopySystemReportAsync(long companyId, string code, long? userId, CancellationToken ct = default);

    /// <summary>Saves a company report (creating it at version 1 if it is new); the replaced version is kept.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The report.</param>
    /// <param name="name">Its name.</param>
    /// <param name="module">Its module.</param>
    /// <param name="definitionJson">The definition (<c>.mrpt</c> JSON).</param>
    /// <param name="userId">Who.</param>
    /// <param name="note">Why.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new version number, or why it was refused.</returns>
    Task<Result<int>> SaveAsync(long companyId, string code, string name, string? module, string definitionJson, long? userId, string? note, CancellationToken ct = default);

    /// <summary>A company report's versions, newest first.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The report.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The history, or why there is none.</returns>
    Task<Result<IReadOnlyList<ReportVersionEntry>>> HistoryAsync(long companyId, string code, CancellationToken ct = default);

    /// <summary>What changed between two versions of a company report.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The report.</param>
    /// <param name="fromVersion">The earlier version.</param>
    /// <param name="toVersion">The later version.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The changes, or why there are none.</returns>
    Task<Result<IReadOnlyList<JsonChange>>> DiffAsync(long companyId, string code, int fromVersion, int toVersion, CancellationToken ct = default);

    /// <summary>Restores an earlier version as the newest one.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The report.</param>
    /// <param name="version">The version to restore.</param>
    /// <param name="userId">Who.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new version number, or why it was refused.</returns>
    Task<Result<int>> RollbackAsync(long companyId, string code, int version, long? userId, CancellationToken ct = default);
}
