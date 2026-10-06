using One4All.Core.Common;

namespace One4All.Core.Budgeting;

/// <summary>What happens when spend would exceed the available budget (brief §4.18.5), mildest first.</summary>
public enum BudgetAction
{
    /// <summary>Let it through.</summary>
    Allow = 0,

    /// <summary>Let it through with a warning.</summary>
    Warn = 1,

    /// <summary>Only with the approval of someone who may override budget blocks.</summary>
    RequireApproval = 2,

    /// <summary>Refuse it.</summary>
    Block = 3,
}

/// <summary>
/// A budget enforcement rule (<c>bud.enforcement_rule</c>): for a department (or any), an account group by code prefix (or any) and
/// documents from a value up, the action taken when the spend would overrun the available budget. The most specific rule applies.
/// </summary>
public sealed class BudgetEnforcementRule : Entity, ICompanyScoped
{
    private BudgetEnforcementRule()
    {
    }

    /// <summary>Creates the rule.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="department">The department, or null for any.</param>
    /// <param name="accountPrefix">The account code prefix, or null for any.</param>
    /// <param name="minimumValue">Applies to spend of at least this value.</param>
    /// <param name="action">The action.</param>
    public BudgetEnforcementRule(long companyId, string? department, string? accountPrefix, decimal minimumValue, BudgetAction action)
    {
        CompanyId = companyId;
        Department = department;
        AccountPrefix = accountPrefix;
        MinimumValue = minimumValue;
        Action = action;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the department, or null for any.</summary>
    public string? Department { get; private set; }

    /// <summary>Gets the account code prefix, or null for any.</summary>
    public string? AccountPrefix { get; private set; }

    /// <summary>Gets the smallest spend it applies to.</summary>
    public decimal MinimumValue { get; private set; }

    /// <summary>Gets or sets the action.</summary>
    public BudgetAction Action { get; set; }
}
