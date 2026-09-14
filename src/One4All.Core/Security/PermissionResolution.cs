namespace One4All.Core.Security;

/// <summary>
/// Pure algorithm that resolves a principal's <see cref="EffectivePermissions"/> from the grants
/// conferred by their roles (direct and via groups) and their explicit overrides. Kept free of I/O
/// so it is exhaustively unit-testable; the infrastructure supplies the assembled inputs.
/// </summary>
public static class PermissionResolution
{
    /// <summary>
    /// Resolves effective permissions. Role grants are unioned per code; grant overrides then add
    /// operations; deny overrides are applied last, so <b>deny always wins</b> (brief §5.1).
    /// </summary>
    /// <param name="roleGrants">Every grant from every role the principal holds, directly or via groups.</param>
    /// <param name="overrides">The principal's explicit grant/deny overrides.</param>
    /// <param name="isSuperAdministrator">Whether the principal is the built-in system administrator.</param>
    /// <returns>The resolved permission set.</returns>
    public static EffectivePermissions Resolve(
        IEnumerable<PermissionGrant> roleGrants,
        IEnumerable<PermissionOverride> overrides,
        bool isSuperAdministrator = false)
    {
        Dictionary<string, OperationFlags> map = new(StringComparer.OrdinalIgnoreCase);

        // 1. Union all role grants.
        foreach (PermissionGrant grant in roleGrants)
        {
            map[grant.PermissionCode] = Get(map, grant.PermissionCode) | grant.Operations;
        }

        // 2. Apply grant overrides (add).
        foreach (PermissionOverride ov in overrides.Where(o => o.IsGrant))
        {
            map[ov.PermissionCode] = Get(map, ov.PermissionCode) | ov.Operations;
        }

        // 3. Apply deny overrides (remove) LAST — deny wins over any grant.
        foreach (PermissionOverride ov in overrides.Where(o => !o.IsGrant))
        {
            map[ov.PermissionCode] = Get(map, ov.PermissionCode) & ~ov.Operations;
        }

        return new EffectivePermissions(map, isSuperAdministrator);
    }

    private static OperationFlags Get(Dictionary<string, OperationFlags> map, string code) =>
        map.TryGetValue(code, out OperationFlags flags) ? flags : OperationFlags.None;
}
