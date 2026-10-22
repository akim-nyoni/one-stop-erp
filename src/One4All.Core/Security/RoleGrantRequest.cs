using One4All.Core.Common;

namespace One4All.Core.Security;

/// <summary>Where a role grant held for a second administrator stands.</summary>
public enum RoleGrantStatus
{
    /// <summary>Waiting for a second administrator.</summary>
    Pending = 0,

    /// <summary>Approved: the role was assigned.</summary>
    Approved = 1,

    /// <summary>Rejected: the user's roles are unchanged.</summary>
    Rejected = 2,
}

/// <summary>
/// A role grant held for a second administrator (<c>sec.role_grant_request</c>): giving the role would break a
/// segregation-of-duties rule the user did not already break, so one administrator asks and another, holding the
/// user-administration right, approves or rejects it. Nothing changes on the user until it is approved.
/// </summary>
public sealed class RoleGrantRequest : AuditableEntity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RoleGrantRequest()
    {
        Conflicts = string.Empty;
    }

    /// <summary>Holds a role grant for a second administrator.</summary>
    /// <param name="userId">The user the role is for.</param>
    /// <param name="roleId">The role.</param>
    /// <param name="scopes">The scopes, as <c>Dimension:value,value;...</c>, or null for unrestricted.</param>
    /// <param name="conflicts">The segregation-of-duties rules the grant would newly break.</param>
    /// <param name="requestedByUserId">The administrator who asked.</param>
    /// <param name="requestedOnUtc">When.</param>
    public RoleGrantRequest(long userId, long roleId, string? scopes, string conflicts, long requestedByUserId, DateTimeOffset requestedOnUtc)
    {
        UserId = userId;
        RoleId = roleId;
        Scopes = scopes;
        Conflicts = conflicts;
        RequestedByUserId = requestedByUserId;
        RequestedOnUtc = requestedOnUtc;
        Status = RoleGrantStatus.Pending;
    }

    /// <summary>Gets the user the role is for.</summary>
    public long UserId { get; private set; }

    /// <summary>Gets the role.</summary>
    public long RoleId { get; private set; }

    /// <summary>Gets the scopes, as <c>Dimension:value,value;...</c>, or null for unrestricted.</summary>
    public string? Scopes { get; private set; }

    /// <summary>Gets the segregation-of-duties rules the grant would newly break.</summary>
    public string Conflicts { get; private set; }

    /// <summary>Gets the administrator who asked.</summary>
    public long RequestedByUserId { get; private set; }

    /// <summary>Gets when it was asked.</summary>
    public DateTimeOffset RequestedOnUtc { get; private set; }

    /// <summary>Gets where it stands.</summary>
    public RoleGrantStatus Status { get; private set; }

    /// <summary>Gets the administrator who decided it.</summary>
    public long? DecidedByUserId { get; private set; }

    /// <summary>Gets when it was decided.</summary>
    public DateTimeOffset? DecidedOnUtc { get; private set; }

    /// <summary>Gets the decision's note.</summary>
    public string? DecisionNote { get; private set; }

    /// <summary>Records the decision.</summary>
    /// <param name="approved">Approved or rejected.</param>
    /// <param name="userId">The administrator who decided.</param>
    /// <param name="onUtc">When.</param>
    /// <param name="note">The note.</param>
    public void Decide(bool approved, long userId, DateTimeOffset onUtc, string? note)
    {
        Status = approved ? RoleGrantStatus.Approved : RoleGrantStatus.Rejected;
        DecidedByUserId = userId;
        DecidedOnUtc = onUtc;
        DecisionNote = note;
    }
}
