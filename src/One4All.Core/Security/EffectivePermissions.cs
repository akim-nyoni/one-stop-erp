namespace One4All.Core.Security;

/// <summary>
/// The resolved set of operations a principal may perform, keyed by permission code. Produced by
/// <see cref="PermissionResolution"/> and consulted on every server-side authorisation check.
/// </summary>
public sealed class EffectivePermissions
{
    private readonly IReadOnlyDictionary<string, OperationFlags> _byCode;

    /// <summary>Creates the set from a resolved code → operations map.</summary>
    /// <param name="byCode">The resolved permissions.</param>
    /// <param name="isSuperAdministrator">When true, <see cref="Has"/> returns true for everything (the built-in system administrator).</param>
    public EffectivePermissions(IReadOnlyDictionary<string, OperationFlags> byCode, bool isSuperAdministrator = false)
    {
        _byCode = byCode;
        IsSuperAdministrator = isSuperAdministrator;
    }

    /// <summary>An empty permission set (no access).</summary>
    public static EffectivePermissions None { get; } =
        new(new Dictionary<string, OperationFlags>(StringComparer.OrdinalIgnoreCase));

    /// <summary>True for the built-in system administrator, who bypasses per-permission checks.</summary>
    public bool IsSuperAdministrator { get; }

    /// <summary>The permission codes the principal has any access to.</summary>
    public IEnumerable<string> Codes => _byCode.Keys;

    /// <summary>Returns the granted operations for a code (or <see cref="OperationFlags.None"/>).</summary>
    /// <param name="permissionCode">The <c>Module.Feature</c> code.</param>
    /// <returns>The operations granted.</returns>
    public OperationFlags OperationsFor(string permissionCode) =>
        _byCode.TryGetValue(permissionCode, out OperationFlags flags) ? flags : OperationFlags.None;

    /// <summary>
    /// Determines whether the principal may perform <paramref name="required"/> operations on
    /// <paramref name="permissionCode"/>. The super administrator always passes.
    /// </summary>
    /// <param name="permissionCode">The <c>Module.Feature</c> code.</param>
    /// <param name="required">The operation(s) required; defaults to <see cref="OperationFlags.View"/>.</param>
    /// <returns>True when permitted.</returns>
    public bool Has(string permissionCode, OperationFlags required = OperationFlags.View)
    {
        if (IsSuperAdministrator)
        {
            return true;
        }

        return OperationsFor(permissionCode).Includes(required);
    }

    /// <summary>True when the principal has any access to at least one feature of a module grouping.</summary>
    /// <param name="module">The module grouping (e.g. <c>Housing</c>).</param>
    /// <returns>True when any feature of the module is accessible.</returns>
    public bool HasAnyInModule(string module)
    {
        if (IsSuperAdministrator)
        {
            return true;
        }

        string prefix = module + ".";
        foreach (KeyValuePair<string, OperationFlags> entry in _byCode)
        {
            if (entry.Value != OperationFlags.None &&
                entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
