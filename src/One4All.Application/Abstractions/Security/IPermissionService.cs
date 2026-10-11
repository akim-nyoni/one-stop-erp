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

    /// <summary>The grants a user inherits from their roles, directly and through groups, before their own overrides.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The role grants.</returns>
    Task<IReadOnlyList<PermissionGrant>> RoleGrantsAsync(long userId, CancellationToken ct = default);

    /// <summary>Sets a user's grant and deny overrides for features (brief §5.2); a feature with neither inherits from the roles again.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="overrides">Each feature's grant and deny operations.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or an unknown user or feature.</returns>
    Task<Core.Common.Result> SetOverridesAsync(long userId, IReadOnlyList<UserOverrideEdit> overrides, CancellationToken ct = default);

    /// <summary>The segregation-of-duties rules in force: the built-in ones as re-tuned or switched off in the rules table, and the rules added there.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rules.</returns>
    Task<IReadOnlyList<SodRule>> SodRulesAsync(CancellationToken ct = default);

    /// <summary>Saves a rule in the SoD rules table: a new conflict, or a built-in rule's code to re-tune it or (inactive) switch it off.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="isActive">Whether it is in force.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or what is wrong with it.</returns>
    Task<Core.Common.Result> SaveSodRuleAsync(SodRule rule, bool isActive, CancellationToken ct = default);
}

/// <summary>A feature's override on the permission grid.</summary>
/// <param name="PermissionCode">The <c>Module.Feature</c> code.</param>
/// <param name="Grant">The operations granted beyond the roles.</param>
/// <param name="Deny">The operations denied whatever the roles give.</param>
public sealed record UserOverrideEdit(string PermissionCode, OperationFlags Grant, OperationFlags Deny);
