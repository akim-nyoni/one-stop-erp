using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>
/// A registered housing cooperative (<c>hse.cooperative</c>, brief §4.10.5 cooperative allocations): stands are allocated
/// to it, and it sub-allocates them to its members - both levels tracked.
/// </summary>
public sealed class Cooperative : AuditableEntity, ICompanyScoped
{
    private readonly List<CooperativeMember> _members = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Cooperative()
    {
        Code = string.Empty;
        Name = string.Empty;
        RegistrationNumber = string.Empty;
    }

    /// <summary>Registers a cooperative.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The cooperative code.</param>
    /// <param name="name">Its name.</param>
    /// <param name="registrationNumber">Its registration number.</param>
    public Cooperative(long companyId, string code, string name, string registrationNumber)
    {
        CompanyId = companyId;
        Code = code;
        Name = name;
        RegistrationNumber = registrationNumber;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The cooperative code.</summary>
    public string Code { get; private set; }

    /// <summary>Its name.</summary>
    public string Name { get; private set; }

    /// <summary>Its registration number.</summary>
    public string RegistrationNumber { get; private set; }

    /// <summary>The cooperative's own receivables account with the council (charges, contributions, statement), once linked.</summary>
    public long? AccountCustomerId { get; set; }

    /// <summary>Its members.</summary>
    public IReadOnlyList<CooperativeMember> Members => _members.AsReadOnly();

    /// <summary>Adds a member.</summary>
    /// <param name="member">The member.</param>
    public void AddMember(CooperativeMember member) => _members.Add(member);

    /// <summary>Corrects the cooperative's name and registration number (its code, members and stands stay).</summary>
    /// <param name="name">The name.</param>
    /// <param name="registrationNumber">The registration number.</param>
    public void Rename(string name, string registrationNumber)
    {
        Name = name;
        RegistrationNumber = registrationNumber;
    }
}

/// <summary>A member of a housing cooperative (<c>hse.cooperative_member</c>).</summary>
public sealed class CooperativeMember : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private CooperativeMember()
    {
        IdNumber = string.Empty;
        FullName = string.Empty;
    }

    /// <summary>Records a member.</summary>
    /// <param name="idNumber">The national ID number (normalised).</param>
    /// <param name="fullName">The full name.</param>
    /// <param name="joinedOn">When they joined.</param>
    public CooperativeMember(string idNumber, string fullName, DateOnly joinedOn)
    {
        IdNumber = idNumber;
        FullName = fullName;
        JoinedOn = joinedOn;
        IsActive = true;
    }

    /// <summary>The cooperative.</summary>
    public long CooperativeId { get; private set; }

    /// <summary>The national ID number.</summary>
    public string IdNumber { get; private set; }

    /// <summary>The full name.</summary>
    public string FullName { get; private set; }

    /// <summary>When they joined.</summary>
    public DateOnly JoinedOn { get; private set; }

    /// <summary>Whether they are still a member.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Records that they left.</summary>
    public void Leave() => IsActive = false;
}

/// <summary>
/// A stand allocated to a cooperative and, once it sub-allocates, to one of its members (<c>hse.cooperative_stand</c>):
/// a stand belongs to one cooperative, and a member holds one stand, enforced in the database.
/// </summary>
public sealed class CooperativeStand : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private CooperativeStand()
    {
    }

    /// <summary>Allocates a stand to a cooperative.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="cooperativeId">The cooperative.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="allocatedOn">When.</param>
    public CooperativeStand(long companyId, long cooperativeId, long standId, DateOnly allocatedOn)
    {
        CompanyId = companyId;
        CooperativeId = cooperativeId;
        StandId = standId;
        AllocatedOn = allocatedOn;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The cooperative.</summary>
    public long CooperativeId { get; private set; }

    /// <summary>The stand.</summary>
    public long StandId { get; private set; }

    /// <summary>When it was allocated to the cooperative.</summary>
    public DateOnly AllocatedOn { get; private set; }

    /// <summary>Who approved it (a second person, not whoever requested it).</summary>
    public long? ApprovedByUserId { get; private set; }

    /// <summary>Records who requested and who approved it.</summary>
    /// <param name="requestedByUserId">The requester.</param>
    /// <param name="approvedByUserId">The approver.</param>
    public void Approved(long requestedByUserId, long approvedByUserId)
    {
        RequestedByUserId = requestedByUserId;
        ApprovedByUserId = approvedByUserId;
    }

    /// <summary>Who requested it.</summary>
    public long? RequestedByUserId { get; private set; }

    /// <summary>The member it was sub-allocated to, if any.</summary>
    public long? MemberId { get; private set; }

    /// <summary>When it was sub-allocated.</summary>
    public DateOnly? SubAllocatedOn { get; private set; }

    /// <summary>Sub-allocates it to a member.</summary>
    /// <param name="memberId">The member.</param>
    /// <param name="date">When.</param>
    public void SubAllocate(long memberId, DateOnly date)
    {
        MemberId = memberId;
        SubAllocatedOn = date;
    }
}

/// <summary>
/// A member's contribution to the cooperative (<c>hse.cooperative_contribution</c>, brief §4.10.8): paid to the council and receipted on the
/// cooperative's account, so the cooperative's statement shows its members' money against what it owes.
/// </summary>
public sealed class CooperativeContribution : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private CooperativeContribution()
    {
        Reference = string.Empty;
    }

    /// <summary>Records a contribution.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="cooperativeId">The cooperative.</param>
    /// <param name="memberId">The member who paid.</param>
    /// <param name="date">When.</param>
    /// <param name="amount">How much.</param>
    /// <param name="reference">The receipt reference.</param>
    public CooperativeContribution(long companyId, long cooperativeId, long memberId, DateOnly date, decimal amount, string reference)
    {
        CompanyId = companyId;
        CooperativeId = cooperativeId;
        MemberId = memberId;
        Date = date;
        Amount = amount;
        Reference = reference;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The cooperative.</summary>
    public long CooperativeId { get; private set; }

    /// <summary>The member who paid.</summary>
    public long MemberId { get; private set; }

    /// <summary>When.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>How much.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The receipt reference.</summary>
    public string Reference { get; private set; }
}
