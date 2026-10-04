using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A new agreement.</summary>
public sealed class AgreementDraft
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The kind of agreement.</summary>
    public AgreementType AgreementType { get; init; }

    /// <summary>The other party.</summary>
    public long PartyCustomerId { get; init; }

    /// <summary>The stand, if any.</summary>
    public long? StandId { get; init; }

    /// <summary>The price or annual rent.</summary>
    public decimal Price { get; init; }

    /// <summary>The payment terms.</summary>
    public string Terms { get; init; } = string.Empty;

    /// <summary>Special conditions.</summary>
    public string? SpecialConditions { get; init; }

    /// <summary>The date it is drawn up.</summary>
    public DateOnly Date { get; init; }
}

/// <summary>A version of an agreement as the register shows it.</summary>
/// <param name="Version">The version number.</param>
/// <param name="Price">The price or annual rent.</param>
/// <param name="AmendmentReason">Why it was amended.</param>
/// <param name="SignedOn">When it was signed.</param>
/// <param name="Witnesses">The witnesses.</param>
/// <param name="InForce">Whether it is the version in force.</param>
/// <param name="Document">The document.</param>
public sealed record AgreementVersionEntry(int Version, decimal Price, string? AmendmentReason, DateOnly? SignedOn, string? Witnesses, bool InForce, string Document);

/// <summary>
/// The housing agreement register (brief §4.10.5): agreements drawn up with a generated, hashed document; signed with two
/// witnesses; amended by a new version that keeps the superseded one; the history of versions; and verification that no
/// stored document has been altered.
/// </summary>
public interface IAgreementRegisterService
{
    /// <summary>Draws up an agreement as version 1, generating its document.</summary>
    /// <param name="draft">The agreement.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The agreement number, or why it was refused.</returns>
    Task<Result<string>> DraftAsync(AgreementDraft draft, CancellationToken ct = default);

    /// <summary>Signs the newest version; it then becomes the version in force.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="agreementNumber">The agreement.</param>
    /// <param name="date">The signature date.</param>
    /// <param name="witnesses">The witnesses (at least two).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SignAsync(long companyId, string agreementNumber, DateOnly date, IReadOnlyList<string> witnesses, CancellationToken ct = default);

    /// <summary>Amends a signed agreement: a new, unsigned version with its own document; the old one stays in force until the new one is signed.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="agreementNumber">The agreement.</param>
    /// <param name="price">The new price or annual rent.</param>
    /// <param name="terms">The new terms.</param>
    /// <param name="specialConditions">The new special conditions.</param>
    /// <param name="reason">Why it is amended (required).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new version number, or why it was refused.</returns>
    Task<Result<int>> AmendAsync(long companyId, string agreementNumber, decimal price, string terms, string? specialConditions, string reason, CancellationToken ct = default);

    /// <summary>Every version of an agreement, oldest first.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="agreementNumber">The agreement.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The versions, or why there are none.</returns>
    Task<Result<IReadOnlyList<AgreementVersionEntry>>> HistoryAsync(long companyId, string agreementNumber, CancellationToken ct = default);

    /// <summary>Re-hashes every stored version's document and lists those that no longer match.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The altered versions as "agreement v{n}"; empty when all are intact.</returns>
    Task<IReadOnlyList<string>> VerifyDocumentsAsync(long companyId, CancellationToken ct = default);
}
