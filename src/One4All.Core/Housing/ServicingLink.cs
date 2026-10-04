using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>How servicing cost is shared across a scheme's stands (brief §4.10.2).</summary>
public enum ServicingBasis
{
    /// <summary>Equal shares.</summary>
    Equal = 1,

    /// <summary>In proportion to stand area.</summary>
    ByArea = 2,

    /// <summary>In proportion to road frontage.</summary>
    ByFrontage = 3,

    /// <summary>In proportion to zoning weight.</summary>
    ByZoningWeight = 4,
}

/// <summary>
/// A servicing contract linked to a scheme (<c>hse.servicing_link</c>, brief §4.10.2 servicing projects / §4.10.10 job
/// costing): the contract's actual cost flows into the scheme's stands on the chosen basis; what has been apportioned so
/// far is kept so each run moves only the cost added since.
/// </summary>
public sealed class ServicingLink : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ServicingLink()
    {
    }

    /// <summary>Links a contract to a scheme.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="schemeId">The scheme.</param>
    /// <param name="jobContractId">The servicing contract.</param>
    /// <param name="basis">How its cost is shared.</param>
    public ServicingLink(long companyId, long schemeId, long jobContractId, ServicingBasis basis)
    {
        CompanyId = companyId;
        SchemeId = schemeId;
        JobContractId = jobContractId;
        Basis = basis;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The scheme.</summary>
    public long SchemeId { get; private set; }

    /// <summary>The servicing contract.</summary>
    public long JobContractId { get; private set; }

    /// <summary>How its cost is shared.</summary>
    public ServicingBasis Basis { get; private set; }

    /// <summary>The cost apportioned to stands so far.</summary>
    public decimal ApportionedToDate { get; private set; }

    /// <summary>Records cost apportioned.</summary>
    /// <param name="amount">The amount.</param>
    public void Apportioned(decimal amount) => ApportionedToDate += amount;
}

/// <summary>A stand's share of a servicing contract's cost from one run (<c>hse.servicing_charge</c>).</summary>
public sealed class ServicingCharge : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ServicingCharge()
    {
        JournalNumber = string.Empty;
    }

    /// <summary>Records a share.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="servicingLinkId">The link.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="amount">The share.</param>
    /// <param name="date">When.</param>
    /// <param name="journalNumber">The capitalising journal.</param>
    public ServicingCharge(long companyId, long servicingLinkId, long standId, decimal amount, DateOnly date, string journalNumber)
    {
        CompanyId = companyId;
        ServicingLinkId = servicingLinkId;
        StandId = standId;
        Amount = amount;
        Date = date;
        JournalNumber = journalNumber;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The link.</summary>
    public long ServicingLinkId { get; private set; }

    /// <summary>The stand.</summary>
    public long StandId { get; private set; }

    /// <summary>The share.</summary>
    public decimal Amount { get; private set; }

    /// <summary>When.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The capitalising journal.</summary>
    public string JournalNumber { get; private set; }
}
