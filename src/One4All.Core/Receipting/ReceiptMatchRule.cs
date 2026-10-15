using One4All.Core.Common;

namespace One4All.Core.Receipting;

/// <summary>Which part of an imported payment line a matching rule reads.</summary>
public enum ReceiptMatchField
{
    /// <summary>The payment reference.</summary>
    Reference = 0,

    /// <summary>The payer's name.</summary>
    PayerName = 1,
}

/// <summary>How a matching rule's pattern is compared.</summary>
public enum ReceiptMatchKind
{
    /// <summary>The text contains the pattern (ignoring case, spaces and dashes).</summary>
    Contains = 0,

    /// <summary>The text starts with the pattern (ignoring case, spaces and dashes).</summary>
    StartsWith = 1,

    /// <summary>The text equals the pattern (ignoring case, spaces and dashes).</summary>
    Equals = 2,

    /// <summary>The text matches a regular expression (ignoring case).</summary>
    Pattern = 3,
}

/// <summary>
/// A council's own rule for matching imported payments (<c>rcp.receipt_match_rule</c>, brief §RCP-8): when the reference or the payer's name fits the pattern,
/// the payment goes to the account — e.g. the employer "MINISTRY OF HEALTH" pays for account EMP-0042, or references starting "WTR" are water accounts.
/// Rules are tried in priority order (lowest first) before the account code and stand number are looked for.
/// </summary>
public sealed class ReceiptMatchRule : AuditableEntity, ICompanyScoped
{
    private ReceiptMatchRule()
    {
        Pattern = string.Empty;
    }

    /// <summary>Creates a rule.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="field">What it reads.</param>
    /// <param name="kind">How it compares.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="customerId">The account the payment goes to.</param>
    /// <param name="priority">Its order (lowest first).</param>
    public ReceiptMatchRule(long companyId, ReceiptMatchField field, ReceiptMatchKind kind, string pattern, long customerId, int priority)
    {
        CompanyId = companyId;
        Field = field;
        Kind = kind;
        Pattern = pattern;
        CustomerId = customerId;
        Priority = priority;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets what the rule reads.</summary>
    public ReceiptMatchField Field { get; private set; }

    /// <summary>Gets how it compares.</summary>
    public ReceiptMatchKind Kind { get; private set; }

    /// <summary>Gets the pattern.</summary>
    public string Pattern { get; private set; }

    /// <summary>Gets the account the payment goes to.</summary>
    public long CustomerId { get; private set; }

    /// <summary>Gets its order (lowest first).</summary>
    public int Priority { get; private set; }
}
