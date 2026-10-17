using One4All.Core.Common;
using One4All.Core.Receipting;

namespace One4All.Application.Abstractions.Receipting;

/// <summary>One line of a payment file to import.</summary>
/// <param name="PayerReference">The payment reference (carries the account, badly typed).</param>
/// <param name="PayerName">The payer name from the file.</param>
/// <param name="Amount">The amount.</param>
/// <param name="ValueDate">The value date.</param>
public readonly record struct ImportLineRequest(string PayerReference, string PayerName, decimal Amount, DateOnly ValueDate);

/// <summary>A request to import a bulk payment file.</summary>
public sealed class ImportRequest
{
    /// <summary>The company.</summary>
    public long CompanyId { get; init; }

    /// <summary>The file name/reference.</summary>
    public string FileReference { get; init; } = string.Empty;

    /// <summary>The file content hash (for duplicate detection).</summary>
    public string FileHash { get; init; } = string.Empty;

    /// <summary>The import source.</summary>
    public ImportSource Source { get; init; } = ImportSource.BankStatement;

    /// <summary>The import date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The bank account the money arrived in (debited).</summary>
    public string BankAccountCode { get; init; } = string.Empty;

    /// <summary>The suspense control account for unmatched money (credited).</summary>
    public string SuspenseAccountCode { get; init; } = string.Empty;

    /// <summary>The lines to import.</summary>
    public IReadOnlyList<ImportLineRequest> Lines { get; init; } = [];
}

/// <summary>The result of an import batch.</summary>
/// <param name="BatchId">The created batch id.</param>
/// <param name="TotalLines">The number of lines.</param>
/// <param name="Matched">The number matched and receipted.</param>
/// <param name="Unmatched">The number parked in suspense.</param>
/// <param name="MatchedAmount">The total matched amount.</param>
/// <param name="UnmatchedAmount">The total unmatched (suspense) amount.</param>
public readonly record struct ImportResult(long BatchId, int TotalLines, int Matched, int Unmatched, decimal MatchedAmount, decimal UnmatchedAmount);

/// <summary>
/// Bulk / file-based receipting (brief §RCP-8). Imports a payment file, auto-matches each line to an
/// account by the payment reference - receipting matched lines against the debtor (never income) and
/// parking unmatched money in the suspense control - and rejects a file already imported (by hash).
/// </summary>
public interface IReceiptImportService
{
    /// <summary>Imports and auto-matches a payment file.</summary>
    /// <param name="request">The import request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The import result, or an error (e.g. a duplicate file).</returns>
    Task<Result<ImportResult>> ImportAsync(ImportRequest request, CancellationToken ct = default);
}
