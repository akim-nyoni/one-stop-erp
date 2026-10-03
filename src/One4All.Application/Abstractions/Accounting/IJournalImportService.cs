using One4All.Core.Common;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>What became of one imported journal.</summary>
/// <param name="Reference">The journal's reference in the file.</param>
/// <param name="JournalNumber">The journal number it was given.</param>
/// <param name="AwaitingApproval">Whether it is held for a second user's approval (over the company's threshold).</param>
public sealed record ImportedJournalResult(string Reference, string JournalNumber, bool AwaitingApproval);

/// <summary>
/// Imports manual journals from a CSV file (brief §4.4), all or nothing: a file with any problem imports nothing, and if
/// any journal is refused on posting (a closed period, an unknown or control account) none is kept. Journals over the
/// company's approval threshold are submitted for a second user's approval instead of posting.
/// </summary>
public interface IJournalImportService
{
    /// <summary>Imports the journals in a file.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="csv">The file content.</param>
    /// <param name="preparedByUserId">Who is importing them.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What became of each journal, or why the file was refused.</returns>
    Task<Result<IReadOnlyList<ImportedJournalResult>>> ImportAsync(long companyId, string csv, long preparedByUserId, CancellationToken ct = default);
}
