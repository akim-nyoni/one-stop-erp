using One4All.Core.Common;

namespace One4All.Core.FixedAssets;

/// <summary>A capital project accumulating cost in capital work in progress until it is brought into use (<c>fa.capital_project</c>).</summary>
public sealed class CapitalProject : AuditableEntity, ICompanyScoped
{
    private readonly List<CapitalProjectCost> _costs = [];

    private CapitalProject()
    {
        Code = string.Empty;
        Name = string.Empty;
        CwipAccountCode = string.Empty;
    }

    /// <summary>Opens a project.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The project code.</param>
    /// <param name="name">The name.</param>
    /// <param name="cwipAccountCode">The capital work-in-progress account its costs sit in.</param>
    /// <param name="budget">The approved budget.</param>
    /// <param name="openedOn">When it opened.</param>
    public CapitalProject(long companyId, string code, string name, string cwipAccountCode, decimal budget, DateOnly openedOn)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        CwipAccountCode = cwipAccountCode;
        Budget = budget;
        OpenedOn = openedOn;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the project code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the CWIP account.</summary>
    public string CwipAccountCode { get; private set; }

    /// <summary>Gets the approved budget.</summary>
    public decimal Budget { get; private set; }

    /// <summary>Gets when it opened.</summary>
    public DateOnly OpenedOn { get; private set; }

    /// <summary>Gets when it was capitalised.</summary>
    public DateOnly? CapitalisedOn { get; private set; }

    /// <summary>Gets the asset it became.</summary>
    public long? AssetId { get; private set; }

    /// <summary>Gets the costs.</summary>
    public IReadOnlyList<CapitalProjectCost> Costs => _costs.AsReadOnly();

    /// <summary>Gets the total cost accumulated.</summary>
    public decimal TotalCost => _costs.Sum(c => c.Amount);

    /// <summary>Gets a value indicating whether the project has been capitalised.</summary>
    public bool IsCapitalised => CapitalisedOn is not null;

    /// <summary>Adds a cost.</summary>
    /// <param name="cost">The cost.</param>
    public void AddCost(CapitalProjectCost cost) => _costs.Add(cost);

    /// <summary>Records the project as brought into use as an asset.</summary>
    /// <param name="date">When.</param>
    /// <param name="assetId">The asset.</param>
    public void Capitalise(DateOnly date, long assetId)
    {
        CapitalisedOn = date;
        AssetId = assetId;
    }
}

/// <summary>One cost charged to a capital project.</summary>
public sealed class CapitalProjectCost : Entity
{
    private CapitalProjectCost()
    {
        Description = string.Empty;
    }

    /// <summary>Records a cost.</summary>
    /// <param name="date">The date.</param>
    /// <param name="description">What it was.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="journalNumber">The journal that put it in CWIP.</param>
    public CapitalProjectCost(DateOnly date, string description, decimal amount, string journalNumber)
    {
        Date = date;
        Description = description;
        Amount = amount;
        JournalNumber = journalNumber;
    }

    /// <summary>Gets the project.</summary>
    public long CapitalProjectId { get; private set; }

    /// <summary>Gets the date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets what it was.</summary>
    public string Description { get; private set; }

    /// <summary>Gets the amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Gets the journal that put it in CWIP.</summary>
    public string? JournalNumber { get; private set; }
}
