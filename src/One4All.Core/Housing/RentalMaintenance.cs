using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>Where a repair stands.</summary>
public enum RepairStatus
{
    /// <summary>Reported and awaiting work.</summary>
    Reported = 0,

    /// <summary>Done and costed.</summary>
    Completed = 1,

    /// <summary>Cancelled (for example a duplicate report).</summary>
    Cancelled = 2,
}

/// <summary>
/// A repair to a council rental unit (<c>hse.unit_repair</c>, brief §4.10.7 maintenance): reported, then completed and
/// costed; where the tenant caused the damage the cost is recharged to the tenant's account.
/// </summary>
public sealed class UnitRepair : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private UnitRepair()
    {
        RepairNumber = string.Empty;
        Description = string.Empty;
    }

    /// <summary>Records a reported fault.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="repairNumber">The repair number.</param>
    /// <param name="unitId">The unit.</param>
    /// <param name="tenancyId">The tenancy at the time, if let.</param>
    /// <param name="reportedOn">When it was reported.</param>
    /// <param name="description">The fault.</param>
    public UnitRepair(long companyId, string repairNumber, long unitId, long? tenancyId, DateOnly reportedOn, string description)
    {
        CompanyId = companyId;
        RepairNumber = repairNumber;
        UnitId = unitId;
        TenancyId = tenancyId;
        ReportedOn = reportedOn;
        Description = description;
        Status = RepairStatus.Reported;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The repair number.</summary>
    public string RepairNumber { get; private set; }

    /// <summary>The unit.</summary>
    public long UnitId { get; private set; }

    /// <summary>The tenancy at the time, if let.</summary>
    public long? TenancyId { get; private set; }

    /// <summary>When it was reported.</summary>
    public DateOnly ReportedOn { get; private set; }

    /// <summary>The fault.</summary>
    public string Description { get; private set; }

    /// <summary>Where it stands.</summary>
    public RepairStatus Status { get; private set; }

    /// <summary>When it was completed.</summary>
    public DateOnly? CompletedOn { get; private set; }

    /// <summary>What it cost.</summary>
    public decimal Cost { get; private set; }

    /// <summary>Whether the tenant caused the damage.</summary>
    public bool TenantCaused { get; private set; }

    /// <summary>The invoice recharging the tenant, if recharged.</summary>
    public string? RechargeInvoiceNumber { get; private set; }

    /// <summary>Records completion.</summary>
    /// <param name="date">When.</param>
    /// <param name="cost">What it cost.</param>
    /// <param name="tenantCaused">Whether the tenant caused the damage.</param>
    /// <param name="rechargeInvoiceNumber">The recharge invoice, if any.</param>
    public void Complete(DateOnly date, decimal cost, bool tenantCaused, string? rechargeInvoiceNumber)
    {
        Status = RepairStatus.Completed;
        CompletedOn = date;
        Cost = cost;
        TenantCaused = tenantCaused;
        RechargeInvoiceNumber = rechargeInvoiceNumber;
    }

    /// <summary>Cancels it.</summary>
    public void Cancel() => Status = RepairStatus.Cancelled;
}

/// <summary>
/// An inspection of a council rental unit (<c>hse.unit_inspection</c>, brief §4.10.7 inspections): the condition grade
/// (1 = poor to 5 = excellent), findings and whether follow-up is needed, with its closure.
/// </summary>
public sealed class UnitInspection : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private UnitInspection()
    {
        Inspector = string.Empty;
        Findings = string.Empty;
    }

    /// <summary>Records an inspection.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="unitId">The unit.</param>
    /// <param name="inspectedOn">When.</param>
    /// <param name="inspector">Who inspected.</param>
    /// <param name="conditionGrade">The condition, 1 (poor) to 5 (excellent).</param>
    /// <param name="findings">What was found.</param>
    /// <param name="followUpRequired">Whether action is needed.</param>
    public UnitInspection(long companyId, long unitId, DateOnly inspectedOn, string inspector, int conditionGrade, string findings, bool followUpRequired)
    {
        CompanyId = companyId;
        UnitId = unitId;
        InspectedOn = inspectedOn;
        Inspector = inspector;
        ConditionGrade = conditionGrade;
        Findings = findings;
        FollowUpRequired = followUpRequired;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The unit.</summary>
    public long UnitId { get; private set; }

    /// <summary>When.</summary>
    public DateOnly InspectedOn { get; private set; }

    /// <summary>Who inspected.</summary>
    public string Inspector { get; private set; }

    /// <summary>The condition, 1 (poor) to 5 (excellent).</summary>
    public int ConditionGrade { get; private set; }

    /// <summary>What was found.</summary>
    public string Findings { get; private set; }

    /// <summary>Whether action is needed.</summary>
    public bool FollowUpRequired { get; private set; }

    /// <summary>When the follow-up was done.</summary>
    public DateOnly? FollowUpDoneOn { get; private set; }

    /// <summary>Closes the follow-up.</summary>
    /// <param name="date">When.</param>
    public void CloseFollowUp(DateOnly date) => FollowUpDoneOn = date;
}
