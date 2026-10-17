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

    /// <summary>
    /// Sets up a sequence (brief §2.4: per company / branch / document type, prefix, padding, year reset, gapless): created when new, otherwise
    /// its format is changed without touching the numbers already given out; a starting number moves the counter forward, never back.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="documentType">The document-type key.</param>
    /// <param name="branchId">The branch, or null for the company-wide sequence.</param>
    /// <param name="setup">The format.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The sequence as it now stands, or why not.</returns>
    Task<Result<NumberSequenceRow>> ConfigureAsync(long companyId, string documentType, long? branchId, NumberSequenceSetup setup, CancellationToken ct = default);

    /// <summary>Every sequence the company has, with its format and next number.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The sequences.</returns>
    Task<IReadOnlyList<NumberSequenceRow>> ListAsync(long companyId, CancellationToken ct = default);
}

/// <summary>A sequence's format.</summary>
/// <param name="Prefix">The prefix (e.g. INV-).</param>
/// <param name="Padding">The digits the number is padded to.</param>
/// <param name="ResetYearly">Whether the counter restarts each year (the year goes into the number: INV-2026-000001).</param>
/// <param name="Gapless">Whether the sequence must have no gaps.</param>
/// <param name="StartAt">The next number to give out, when the counter is to move forward (null: leave it).</param>
public sealed record NumberSequenceSetup(string Prefix, int Padding = 5, bool ResetYearly = false, bool Gapless = true, long? StartAt = null);

/// <summary>A sequence as it stands.</summary>
/// <param name="DocumentType">The document-type key.</param>
/// <param name="BranchId">The branch, or null.</param>
/// <param name="Prefix">The prefix.</param>
/// <param name="Padding">The padding.</param>
/// <param name="ResetYearly">Whether it restarts each year.</param>
/// <param name="Gapless">Whether it is gapless.</param>
/// <param name="NextValue">The next number.</param>
/// <param name="CurrentYear">The year the counter is in (a yearly sequence).</param>
public sealed record NumberSequenceRow(string DocumentType, long? BranchId, string Prefix, int Padding, bool ResetYearly, bool Gapless, long NextValue, int CurrentYear);
