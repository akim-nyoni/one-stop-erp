using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>A cost element of a housing scheme (brief §14 H84).</summary>
public enum SchemeCostElement
{
    /// <summary>The land (acquisition cost apportioned to the scheme's stands).</summary>
    Land = 1,

    /// <summary>Bulk servicing (apportioned from servicing contracts).</summary>
    Servicing = 2,

    /// <summary>Professional fees (surveyors, engineers, planners, conveyancers).</summary>
    ProfessionalFees = 3,

    /// <summary>Overheads capitalised to the scheme.</summary>
    Overheads = 4,
}

/// <summary>A scheme's approved budget for one cost element (<c>hse.scheme_budget</c>, brief §14 H84).</summary>
public sealed class SchemeBudget : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SchemeBudget()
    {
    }

    /// <summary>Sets a budget.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="schemeId">The scheme.</param>
    /// <param name="element">The cost element.</param>
    /// <param name="amount">The budget.</param>
    public SchemeBudget(long companyId, long schemeId, SchemeCostElement element, decimal amount)
    {
        CompanyId = companyId;
        SchemeId = schemeId;
        Element = element;
        Amount = amount;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The scheme.</summary>
    public long SchemeId { get; private set; }

    /// <summary>The cost element.</summary>
    public SchemeCostElement Element { get; private set; }

    /// <summary>The budget.</summary>
    public decimal Amount { get; set; }
}

/// <summary>
/// A professional fee or overhead capitalised to a scheme (<c>hse.scheme_cost</c>, brief §14 H84): posted to the stands
/// account and spread equally over the scheme's unsold stands, so it is recovered through their cost of sale.
/// </summary>
public sealed class SchemeCost : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SchemeCost()
    {
        Reference = string.Empty;
        Description = string.Empty;
        JournalNumber = string.Empty;
    }

    /// <summary>Records a capitalised cost.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="schemeId">The scheme.</param>
    /// <param name="element">Professional fees or overheads.</param>
    /// <param name="date">When.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="reference">The invoice or voucher.</param>
    /// <param name="description">What it was.</param>
    /// <param name="standsCharged">How many stands it was spread over.</param>
    /// <param name="journalNumber">The journal.</param>
    public SchemeCost(long companyId, long schemeId, SchemeCostElement element, DateOnly date, decimal amount, string reference, string description, int standsCharged, string journalNumber)
    {
        CompanyId = companyId;
        SchemeId = schemeId;
        Element = element;
        Date = date;
        Amount = amount;
        Reference = reference;
        Description = description;
        StandsCharged = standsCharged;
        JournalNumber = journalNumber;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The scheme.</summary>
    public long SchemeId { get; private set; }

    /// <summary>Professional fees or overheads.</summary>
    public SchemeCostElement Element { get; private set; }

    /// <summary>When.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The invoice or voucher.</summary>
    public string Reference { get; private set; }

    /// <summary>What it was.</summary>
    public string Description { get; private set; }

    /// <summary>How many stands it was spread over.</summary>
    public int StandsCharged { get; private set; }

    /// <summary>The journal.</summary>
    public string JournalNumber { get; private set; }
}
