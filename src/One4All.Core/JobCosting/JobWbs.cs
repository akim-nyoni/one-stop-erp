using One4All.Core.Common;

namespace One4All.Core.JobCosting;

/// <summary>
/// An element of a contract's work breakdown structure (<c>job.wbs_element</c>). Codes are dotted ("1", "1.2", "1.2.3"): the parent of
/// "1.2" is "1". Phases group activities; an activity (a leaf) carries the budget and the cost head its costs are captured under.
/// </summary>
public sealed class JobWbsElement : Entity, ICompanyScoped
{
    private JobWbsElement()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates the element.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="jobContractId">The contract.</param>
    /// <param name="code">The dotted code.</param>
    /// <param name="name">The name.</param>
    /// <param name="budgetCost">The budget (activities only).</param>
    /// <param name="costHead">The cost head its costs are captured under (activities only).</param>
    public JobWbsElement(long companyId, long jobContractId, string code, string name, decimal budgetCost, string? costHead)
    {
        CompanyId = companyId;
        JobContractId = jobContractId;
        Code = code;
        Name = name;
        BudgetCost = budgetCost;
        CostHead = costHead;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the contract.</summary>
    public long JobContractId { get; private set; }

    /// <summary>Gets the dotted code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets or sets the budget.</summary>
    public decimal BudgetCost { get; set; }

    /// <summary>Gets the cost head its costs are captured under.</summary>
    public string? CostHead { get; private set; }

    /// <summary>Gets or sets the estimate to complete given by the site (null: budget less actual and committed).</summary>
    public decimal? EstimateToComplete { get; set; }

    /// <summary>Gets or sets when the estimate was last given.</summary>
    public DateOnly? EstimatedOn { get; set; }

    /// <summary>The parent's code, or null for a top-level element.</summary>
    public string? ParentCode => Code.Contains('.', StringComparison.Ordinal) ? Code[..Code.LastIndexOf('.')] : null;
}
