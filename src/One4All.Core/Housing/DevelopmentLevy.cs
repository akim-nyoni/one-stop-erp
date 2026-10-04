using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>When a development levy is raised (brief §4.10.2: at allocation or at transfer).</summary>
public enum LevyTrigger
{
    /// <summary>When an allocation is accepted.</summary>
    Allocation = 1,

    /// <summary>On a change of ownership.</summary>
    Transfer = 2,
}

/// <summary>How a development levy is measured.</summary>
public enum LevyBasis
{
    /// <summary>A flat amount per stand.</summary>
    Flat = 1,

    /// <summary>An amount per square metre of the stand.</summary>
    PerSquareMetre = 2,

    /// <summary>A percentage of the price or consideration.</summary>
    PercentOfPrice = 3,
}

/// <summary>
/// A configurable development / infrastructure charge (<c>hse.development_levy</c>, brief §4.10.2: endowment fee,
/// intrinsic land value charge, infrastructure development levy, connection fees), raised on the sales ledger when its
/// trigger occurs.
/// </summary>
public sealed class DevelopmentLevy : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private DevelopmentLevy()
    {
        Code = string.Empty;
        Name = string.Empty;
        IncomeAccountCode = string.Empty;
    }

    /// <summary>Defines a levy.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The levy code.</param>
    /// <param name="name">Its name.</param>
    /// <param name="trigger">When it is raised.</param>
    /// <param name="basis">How it is measured.</param>
    /// <param name="rate">The amount, rate per m² or percentage.</param>
    /// <param name="incomeAccountCode">The income account credited.</param>
    public DevelopmentLevy(long companyId, string code, string name, LevyTrigger trigger, LevyBasis basis, decimal rate, string incomeAccountCode)
    {
        CompanyId = companyId;
        Code = code;
        Name = name;
        Trigger = trigger;
        Basis = basis;
        Rate = rate;
        IncomeAccountCode = incomeAccountCode;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The levy code.</summary>
    public string Code { get; private set; }

    /// <summary>Its name.</summary>
    public string Name { get; private set; }

    /// <summary>When it is raised.</summary>
    public LevyTrigger Trigger { get; private set; }

    /// <summary>How it is measured.</summary>
    public LevyBasis Basis { get; private set; }

    /// <summary>The amount, rate per m² or percentage.</summary>
    public decimal Rate { get; private set; }

    /// <summary>The income account credited.</summary>
    public string IncomeAccountCode { get; private set; }

    /// <summary>Whether it is still raised.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Stops raising it (charges already raised stand).</summary>
    public void Deactivate() => IsActive = false;
}

/// <summary>
/// A development levy raised (<c>hse.levy_charge</c>): one per levy per event (an allocation certificate or a transfer
/// application), enforced in the database, so re-running a trigger never charges twice.
/// </summary>
public sealed class LevyCharge : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LevyCharge()
    {
        EventReference = string.Empty;
        DocumentNumber = string.Empty;
    }

    /// <summary>Records a charge.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="levyId">The levy.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="customerId">Who was charged.</param>
    /// <param name="eventReference">The certificate or transfer it was raised on.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="documentNumber">The invoice.</param>
    /// <param name="chargedOn">When.</param>
    public LevyCharge(long companyId, long levyId, long standId, long customerId, string eventReference, decimal amount, string documentNumber, DateOnly chargedOn)
    {
        CompanyId = companyId;
        LevyId = levyId;
        StandId = standId;
        CustomerId = customerId;
        EventReference = eventReference;
        Amount = amount;
        DocumentNumber = documentNumber;
        ChargedOn = chargedOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The levy.</summary>
    public long LevyId { get; private set; }

    /// <summary>The stand.</summary>
    public long StandId { get; private set; }

    /// <summary>Who was charged.</summary>
    public long CustomerId { get; private set; }

    /// <summary>The certificate or transfer it was raised on.</summary>
    public string EventReference { get; private set; }

    /// <summary>The amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The invoice.</summary>
    public string DocumentNumber { get; private set; }

    /// <summary>When.</summary>
    public DateOnly ChargedOn { get; private set; }
}
