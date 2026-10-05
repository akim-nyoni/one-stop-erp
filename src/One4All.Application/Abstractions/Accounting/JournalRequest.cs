using One4All.Core.Accounting;

namespace One4All.Application.Abstractions.Accounting;

/// <summary>The side of a posting line.</summary>
public enum JournalSide
{
    /// <summary>A debit.</summary>
    Debit = 1,

    /// <summary>A credit.</summary>
    Credit = 2,
}

/// <summary>
/// A request to post one balanced journal through the posting engine. Amounts may be supplied in
/// transaction currency only (the engine converts to base using <see cref="ExchangeRate"/> or a looked-up
/// rate) or with an explicit base amount. All lines must balance in base currency, and per currency in
/// transaction currency.
/// </summary>
public sealed class JournalRequest
{
    /// <summary>The company to post into.</summary>
    public long CompanyId { get; init; }

    /// <summary>The module raising the posting (e.g. "GL", "AR", "POS").</summary>
    public string SourceModule { get; init; } = "GL";

    /// <summary>The accounting date.</summary>
    public DateOnly PostingDate { get; init; }

    /// <summary>The transaction currency of the document.</summary>
    public CurrencyCode TransactionCurrency { get; init; }

    /// <summary>The source document type.</summary>
    public string? DocumentType { get; init; }

    /// <summary>The source document number.</summary>
    public string? DocumentNumber { get; init; }

    /// <summary>A user reference.</summary>
    public string? Reference { get; init; }

    /// <summary>A narrative/description.</summary>
    public string? Narrative { get; init; }

    /// <summary>
    /// When true, control accounts are rejected (manual GL journals may not touch a control account).
    /// Sub-ledger postings set this false.
    /// </summary>
    public bool EnforceManualJournalRules { get; init; }

    /// <summary>When true, the journal is queued as deferred rather than posted live.</summary>
    public bool Deferred { get; init; }

    /// <summary>
    /// When set, the journal is submitted for approval by this user (the preparer): it is numbered and held, unposted,
    /// until a different user approves it.
    /// </summary>
    public long? SubmittedByUserId { get; init; }

    /// <summary>An idempotency key so a retried post (e.g. POS sync) does not double-post.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>The posting lines.</summary>
    public IReadOnlyList<JournalLineRequest> Lines { get; init; } = [];

    /// <summary>Gets why fund control may be overridden for this posting (with <see cref="BudgetOverrideAuthorisedByUserId"/>).</summary>
    public string? BudgetOverrideReason { get; init; }

    /// <summary>Gets the supervisor authorising a fund-control override (never the preparer).</summary>
    public long? BudgetOverrideAuthorisedByUserId { get; init; }
}

/// <summary>One line of a <see cref="JournalRequest"/>.</summary>
public sealed class JournalLineRequest
{
    /// <summary>The base nominal account code.</summary>
    public string AccountCode { get; init; } = string.Empty;

    /// <summary>The cost-centre code (optional).</summary>
    public string? CostCentre { get; init; }

    /// <summary>The department code (optional).</summary>
    public string? Department { get; init; }

    /// <summary>The side of the entry.</summary>
    public JournalSide Side { get; init; }

    /// <summary>The amount in transaction currency (non-negative).</summary>
    public decimal TransactionAmount { get; init; }

    /// <summary>
    /// The exchange rate to base currency. When null and the transaction currency differs from base, the
    /// engine looks up the daily rate for the posting date.
    /// </summary>
    public decimal? ExchangeRate { get; init; }

    /// <summary>
    /// The base-currency amount. When null, the engine computes it as <c>TransactionAmount × rate</c>.
    /// Supplying it lets the caller control rounding on the base leg.
    /// </summary>
    public decimal? BaseAmount { get; init; }

    /// <summary>Optional line narrative.</summary>
    public string? Narrative { get; init; }

    /// <summary>The tax code this line relates to — set on tax-account lines so VAT can be analysed by rate/code.</summary>
    public string? TaxCode { get; init; }

    /// <summary>Builds the composite account key for this line.</summary>
    /// <returns>The account key.</returns>
    public AccountKey ToKey() => new(AccountCode, CostCentre, Department);
}
