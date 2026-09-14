namespace One4All.Core.Security;

/// <summary>
/// A grant of operations on a permission code, coming from a role (directly assigned to the user or
/// inherited via a group). Multiple grants for the same code are unioned during resolution.
/// </summary>
/// <param name="PermissionCode">The <c>Module.Feature</c> code.</param>
/// <param name="Operations">The operations the grant confers.</param>
public readonly record struct PermissionGrant(string PermissionCode, OperationFlags Operations);

/// <summary>
/// An explicit per-user override on a permission code. A <see cref="IsGrant"/> override adds
/// operations; a deny override removes them. Per the brief, <b>deny always wins</b>: denies are
/// applied after grants so a denied operation cannot be re-granted by any source.
/// </summary>
/// <param name="PermissionCode">The <c>Module.Feature</c> code.</param>
/// <param name="Operations">The operations affected.</param>
/// <param name="IsGrant">True to add the operations; false to deny (remove) them.</param>
public readonly record struct PermissionOverride(string PermissionCode, OperationFlags Operations, bool IsGrant);
