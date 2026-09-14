using One4All.Core.Security;

namespace One4All.Application.Abstractions.Security;

/// <summary>
/// Resolves a user's effective permissions by assembling their role grants (direct and via groups) and
/// overrides, then applying <see cref="PermissionResolution"/>. Server-side authorisation consults the
/// result; the UI uses it to build menus.
/// </summary>
public interface IPermissionService
{
    /// <summary>Resolves the effective permissions for a user.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The user's effective permissions, or <see cref="EffectivePermissions.None"/> if the user is unknown/inactive.</returns>
    Task<EffectivePermissions> ResolveAsync(long userId, CancellationToken ct = default);

    /// <summary>Returns the segregation-of-duties violations implied by a user's effective permissions.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The violated SoD rules.</returns>
    Task<IReadOnlyList<SodRule>> GetSodViolationsAsync(long userId, CancellationToken ct = default);
}
