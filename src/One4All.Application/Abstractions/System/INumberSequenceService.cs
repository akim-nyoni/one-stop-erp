using One4All.Core.Common;

namespace One4All.Application.Abstractions.Numbering;

/// <summary>
/// Allocates the next formatted document number for a company/document-type sequence (brief §3.5): loads
/// (or creates) the sequence, allocates and increments it, and persists it. Gives every module gapless,
/// prefixed, optionally year-resetting numbering without callers inventing their own numbers.
/// </summary>
public interface INumberSequenceService
{
    /// <summary>
    /// Allocates the next number for a document type. If no sequence is configured for the
    /// company/type/branch, a sensible default (<c>{documentType}-</c> prefix, 5-digit padding) is created.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="documentType">The document-type key (e.g. <c>AR.Invoice</c>).</param>
    /// <param name="date">The document date (its year drives a yearly-reset sequence).</param>
    /// <param name="branchId">Optional branch scope; null for the company-wide sequence.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The allocated formatted number.</returns>
    Task<Result<string>> AllocateAsync(long companyId, string documentType, DateOnly date, long? branchId = null, CancellationToken ct = default);
}
