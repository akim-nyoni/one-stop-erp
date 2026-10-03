using One4All.Core.Common;

namespace One4All.Core.Accounting;

/// <summary>How an overhead pool is shared across the receiving cost centres (brief §4.4).</summary>
public enum AllocationBasis
{
    /// <summary>A fixed percentage per receiver; the percentages must add up to 100.</summary>
    FixedPercent = 1,

    /// <summary>In proportion to each receiver's headcount.</summary>
    Headcount = 2,

    /// <summary>In proportion to each receiver's floor area.</summary>
    FloorArea = 3,

    /// <summary>In proportion to each receiver's usage of the service (hours, units, kWh), updated before each run.</summary>
    UsageDriver = 4,
}

/// <summary>
/// An overhead recharge rule (<c>gl.allocation_rule</c>, brief §4.4): at period end the net cost booked to the source
/// account in the source cost centre (the pool) is credited out and debited to the receiving cost centres in proportion
/// to their drivers. Rules run in sequence order, so a later rule can pass on costs an earlier one recharged into its
/// pool (step-down allocation).
/// </summary>
public sealed class AllocationRule : AuditableEntity, ICompanyScoped
{
    private readonly List<AllocationRuleTarget> _targets = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private AllocationRule()
    {
        Code = string.Empty;
        Name = string.Empty;
        SourceAccountCode = string.Empty;
        SourceCostCentre = string.Empty;
    }

    /// <summary>Creates a rule.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The rule code.</param>
    /// <param name="name">The rule name.</param>
    /// <param name="sequence">The order rules run in.</param>
    /// <param name="basis">The allocation basis.</param>
    /// <param name="sourceAccountCode">The account holding the overhead.</param>
    /// <param name="sourceCostCentre">The cost centre whose overhead is recharged.</param>
    /// <param name="targetAccountCode">The account the receivers are charged on; null for the source account.</param>
    public AllocationRule(long companyId, string code, string name, int sequence, AllocationBasis basis, string sourceAccountCode, string sourceCostCentre, string? targetAccountCode)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        Sequence = sequence;
        Basis = basis;
        SourceAccountCode = sourceAccountCode;
        SourceCostCentre = sourceCostCentre.Trim().ToUpperInvariant();
        TargetAccountCode = targetAccountCode;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The rule code.</summary>
    public string Code { get; private set; }

    /// <summary>The rule name.</summary>
    public string Name { get; set; }

    /// <summary>The order rules run in, lowest first.</summary>
    public int Sequence { get; set; }

    /// <summary>The allocation basis.</summary>
    public AllocationBasis Basis { get; private set; }

    /// <summary>The account holding the overhead.</summary>
    public string SourceAccountCode { get; private set; }

    /// <summary>The cost centre whose overhead is recharged.</summary>
    public string SourceCostCentre { get; private set; }

    /// <summary>The account receivers are charged on; null to charge them on the source account.</summary>
    public string? TargetAccountCode { get; private set; }

    /// <summary>Whether the rule runs.</summary>
    public bool IsActive { get; set; }

    /// <summary>The receiving cost centres and their drivers.</summary>
    public IReadOnlyList<AllocationRuleTarget> Targets => _targets.AsReadOnly();

    /// <summary>The account receivers are charged on.</summary>
    public string ChargeAccountCode => TargetAccountCode ?? SourceAccountCode;

    /// <summary>Replaces the receivers and their drivers (say, this month's usage).</summary>
    /// <param name="targets">The receivers: cost centre, optional department, driver.</param>
    public void SetTargets(IEnumerable<(string CostCentre, string? Department, decimal Driver)> targets)
    {
        _targets.Clear();
        foreach ((string costCentre, string? department, decimal driver) in targets)
        {
            _targets.Add(new AllocationRuleTarget(costCentre.Trim().ToUpperInvariant(), string.IsNullOrWhiteSpace(department) ? null : department.Trim().ToUpperInvariant(), driver));
        }
    }
}

/// <summary>One receiving cost centre of an allocation rule (<c>gl.allocation_rule_target</c>).</summary>
public sealed class AllocationRuleTarget : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private AllocationRuleTarget()
    {
        CostCentre = string.Empty;
    }

    /// <summary>Creates a receiver.</summary>
    /// <param name="costCentre">The receiving cost centre.</param>
    /// <param name="department">The receiving department, if any.</param>
    /// <param name="driver">Its driver: a percentage, headcount, area or usage quantity.</param>
    public AllocationRuleTarget(string costCentre, string? department, decimal driver)
    {
        CostCentre = costCentre;
        Department = department;
        Driver = driver;
    }

    /// <summary>The owning rule.</summary>
    public long AllocationRuleId { get; private set; }

    /// <summary>The receiving cost centre.</summary>
    public string CostCentre { get; private set; }

    /// <summary>The receiving department, if any.</summary>
    public string? Department { get; private set; }

    /// <summary>The driver.</summary>
    public decimal Driver { get; private set; }
}
