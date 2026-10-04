using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>Whose share of a developer agreement's stands a stand falls in.</summary>
public enum StandQuota
{
    /// <summary>The council's quota: handed over to the council for its own allocation.</summary>
    Council = 1,

    /// <summary>The developer's quota: the developer's to sell.</summary>
    Developer = 2,
}

/// <summary>
/// A developer / partner agreement on a housing scheme (<c>hse.developer_agreement</c>, brief §4.10.8): the council
/// contributes land, the developer services and builds; the stands are split between a council quota and a developer
/// quota; the developer must deliver the council's stands by the deadline or pay a penalty per stand per month late.
/// </summary>
public sealed class DeveloperAgreement : AuditableEntity, ICompanyScoped
{
    private readonly List<DeveloperAgreementStand> _stands = [];
    private readonly List<DeliveryMilestone> _milestones = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private DeveloperAgreement()
    {
        AgreementNumber = string.Empty;
    }

    /// <summary>Records an agreement.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="agreementNumber">The agreement number.</param>
    /// <param name="schemeId">The scheme.</param>
    /// <param name="developerCustomerId">The developer's account (penalties are invoiced to it).</param>
    /// <param name="councilQuota">The stands the developer must hand to the council.</param>
    /// <param name="developerQuota">The stands the developer keeps.</param>
    /// <param name="signedOn">When it was signed.</param>
    /// <param name="deliveryDeadline">When the council's stands are due.</param>
    /// <param name="penaltyPerStandPerMonth">The penalty per council stand per month (or part) late.</param>
    public DeveloperAgreement(long companyId, string agreementNumber, long schemeId, long developerCustomerId, int councilQuota, int developerQuota,
        DateOnly signedOn, DateOnly deliveryDeadline, decimal penaltyPerStandPerMonth)
    {
        CompanyId = companyId;
        AgreementNumber = agreementNumber;
        SchemeId = schemeId;
        DeveloperCustomerId = developerCustomerId;
        CouncilQuota = councilQuota;
        DeveloperQuota = developerQuota;
        SignedOn = signedOn;
        DeliveryDeadline = deliveryDeadline;
        PenaltyPerStandPerMonth = penaltyPerStandPerMonth;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The agreement number.</summary>
    public string AgreementNumber { get; private set; }

    /// <summary>The scheme.</summary>
    public long SchemeId { get; private set; }

    /// <summary>The developer's account.</summary>
    public long DeveloperCustomerId { get; private set; }

    /// <summary>The stands the developer must hand to the council.</summary>
    public int CouncilQuota { get; private set; }

    /// <summary>The stands the developer keeps.</summary>
    public int DeveloperQuota { get; private set; }

    /// <summary>When it was signed.</summary>
    public DateOnly SignedOn { get; private set; }

    /// <summary>When the council's stands are due.</summary>
    public DateOnly DeliveryDeadline { get; private set; }

    /// <summary>The penalty per council stand per month (or part) late.</summary>
    public decimal PenaltyPerStandPerMonth { get; private set; }

    /// <summary>The penalties invoiced so far.</summary>
    public decimal PenaltiesCharged { get; private set; }

    /// <summary>The stands assigned to it.</summary>
    public IReadOnlyList<DeveloperAgreementStand> Stands => _stands.AsReadOnly();

    /// <summary>Its delivery milestones.</summary>
    public IReadOnlyList<DeliveryMilestone> Milestones => _milestones.AsReadOnly();

    /// <summary>Assigns a stand to a quota.</summary>
    /// <param name="stand">The assignment.</param>
    public void AddStand(DeveloperAgreementStand stand) => _stands.Add(stand);

    /// <summary>Adds a delivery milestone.</summary>
    /// <param name="milestone">The milestone.</param>
    public void AddMilestone(DeliveryMilestone milestone) => _milestones.Add(milestone);

    /// <summary>Records penalties invoiced.</summary>
    /// <param name="amount">The amount.</param>
    public void ChargePenalty(decimal amount) => PenaltiesCharged += amount;
}

/// <summary>A stand on a developer agreement, in one quota, and when it was handed over (<c>hse.developer_agreement_stand</c>).</summary>
public sealed class DeveloperAgreementStand : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private DeveloperAgreementStand()
    {
    }

    /// <summary>Assigns a stand.</summary>
    /// <param name="standId">The stand.</param>
    /// <param name="quota">The quota it falls in.</param>
    /// <param name="assignedOn">When.</param>
    public DeveloperAgreementStand(long standId, StandQuota quota, DateOnly assignedOn)
    {
        StandId = standId;
        Quota = quota;
        AssignedOn = assignedOn;
    }

    /// <summary>The agreement.</summary>
    public long DeveloperAgreementId { get; private set; }

    /// <summary>The stand.</summary>
    public long StandId { get; private set; }

    /// <summary>The quota it falls in.</summary>
    public StandQuota Quota { get; private set; }

    /// <summary>When it was assigned.</summary>
    public DateOnly AssignedOn { get; private set; }

    /// <summary>When it was handed over, serviced (and built where agreed).</summary>
    public DateOnly? HandedOverOn { get; private set; }

    /// <summary>Records the hand-over.</summary>
    /// <param name="date">When.</param>
    public void HandOver(DateOnly date) => HandedOverOn = date;
}

/// <summary>A delivery milestone of a developer agreement (<c>hse.delivery_milestone</c>).</summary>
public sealed class DeliveryMilestone : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private DeliveryMilestone()
    {
        Description = string.Empty;
    }

    /// <summary>Adds a milestone.</summary>
    /// <param name="description">What is to be delivered.</param>
    /// <param name="dueOn">When it is due.</param>
    public DeliveryMilestone(string description, DateOnly dueOn)
    {
        Description = description;
        DueOn = dueOn;
    }

    /// <summary>The agreement.</summary>
    public long DeveloperAgreementId { get; private set; }

    /// <summary>What is to be delivered.</summary>
    public string Description { get; private set; }

    /// <summary>When it is due.</summary>
    public DateOnly DueOn { get; private set; }

    /// <summary>When it was achieved.</summary>
    public DateOnly? CompletedOn { get; private set; }

    /// <summary>Records it achieved.</summary>
    /// <param name="date">When.</param>
    public void Complete(DateOnly date) => CompletedOn = date;
}
