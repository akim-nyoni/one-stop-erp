using One4All.Core.Common;

namespace One4All.Core.Banking;

/// <summary>Which statement lines a bank rule applies to by direction.</summary>
public enum BankRuleDirection
{
    /// <summary>Money in and money out.</summary>
    Either = 0,

    /// <summary>Money in only.</summary>
    MoneyIn = 1,

    /// <summary>Money out only.</summary>
    MoneyOut = 2,
}

/// <summary>
/// A bank rule (brief §4.7, <c>cb.bank_rule</c>): statement lines whose description contains a phrase (and,
/// optionally, fall in a direction and amount range) are posted straight to a nominal account - bank charges,
/// interest, standing orders - and matched, without anyone keying them.
/// </summary>
public sealed class BankRule : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private BankRule()
    {
        Name = string.Empty;
        DescriptionContains = string.Empty;
        AccountCode = string.Empty;
    }

    /// <summary>Creates a rule.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="bankAccountId">The bank account the rule applies to, or null for every bank account.</param>
    /// <param name="name">A name for the rule.</param>
    /// <param name="descriptionContains">The phrase the statement description must contain (case-insensitive).</param>
    /// <param name="direction">The direction filter.</param>
    /// <param name="accountCode">The account to post to.</param>
    public BankRule(long companyId, long? bankAccountId, string name, string descriptionContains, BankRuleDirection direction, string accountCode)
    {
        CompanyId = companyId;
        BankAccountId = bankAccountId;
        Name = name.Trim();
        DescriptionContains = descriptionContains.Trim();
        Direction = direction;
        AccountCode = accountCode.Trim().ToUpperInvariant();
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The bank account the rule applies to, or null for all.</summary>
    public long? BankAccountId { get; private set; }

    /// <summary>The rule name.</summary>
    public string Name { get; set; }

    /// <summary>The phrase the description must contain.</summary>
    public string DescriptionContains { get; private set; }

    /// <summary>The direction filter.</summary>
    public BankRuleDirection Direction { get; private set; }

    /// <summary>Optional lower bound on the absolute amount.</summary>
    public decimal? MinAmount { get; set; }

    /// <summary>Optional upper bound on the absolute amount.</summary>
    public decimal? MaxAmount { get; set; }

    /// <summary>The account posted to.</summary>
    public string AccountCode { get; private set; }

    /// <summary>Gets or sets the tax code when the money the rule posts includes VAT (its net to the account, the VAT to the code's account).</summary>
    public string? TaxCode { get; set; }

    /// <summary>The narrative used on the posting, or null to keep the bank's description.</summary>
    public string? Narrative { get; set; }

    /// <summary>Whether the rule is applied.</summary>
    public bool IsActive { get; set; }

    /// <summary>Lower numbers are tried first when several rules match a line.</summary>
    public int Priority { get; set; } = 100;
}
