using One4All.Core.Security;

namespace One4All.Application.Abstractions.Security;

/// <summary>
/// Ambient information about the signed-in user and their active company, used for authorisation,
/// company-scoped query filtering, and audit stamping. Populated per request (API) or per session
/// (embedded desktop).
/// </summary>
public interface ICurrentUser
{
    /// <summary>The signed-in user id, or null when unauthenticated.</summary>
    long? UserId { get; }

    /// <summary>The signed-in user's display/login name.</summary>
    string UserName { get; }

    /// <summary>The active company id, or null when none selected.</summary>
    long? CompanyId { get; }

    /// <summary>True when a user is signed in.</summary>
    bool IsAuthenticated { get; }

    /// <summary>The signed-in user's resolved permissions.</summary>
    EffectivePermissions Permissions { get; }

    /// <summary>The terminal/workstation identifier, for audit.</summary>
    string? Terminal { get; }

    /// <summary>Where the request comes from: the client IP address (API) or the machine name (desktop), for the login and audit trails.</summary>
    string? ClientAddress => null;

    /// <summary>Convenience check for a permission and operation.</summary>
    /// <param name="permissionCode">The <c>Module.Feature</c> code.</param>
    /// <param name="required">The required operation(s).</param>
    /// <returns>True when permitted.</returns>
    bool Can(string permissionCode, OperationFlags required = OperationFlags.View) =>
        Permissions.Has(permissionCode, required);
}
