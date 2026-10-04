using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>
/// A registered housing cooperative (<c>hse.cooperative</c>, brief §4.10.5 cooperative allocations): stands are allocated
/// to it, and it sub-allocates them to its members — both levels tracked.
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

    /// <summary>Its members.</summary>
    public IReadOnlyList<CooperativeMember> Members => _members.AsReadOnly();

    /// <summary>Adds a member.</summary>
    /// <param name="member">The member.</param>
    public void AddMember(CooperativeMember member) => _members.Add(member);
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
