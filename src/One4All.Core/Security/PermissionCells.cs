namespace One4All.Core.Security;

/// <summary>
/// The permission grid's per-user cells (brief §5.2: "user-level Grant/Deny overrides on top of roles"). Each operation of a feature is
/// inherited from the user's roles (null), explicitly granted (true) or explicitly denied (false). Pure.
/// </summary>
public static class PermissionCells
{
    /// <summary>Every operation flag in grid column order.</summary>
    public static readonly OperationFlags[] Columns = [.. Enum.GetValues<OperationFlags>().Where(f => f != OperationFlags.None)];

    /// <summary>A cell's override state.</summary>
    /// <param name="flag">The operation.</param>
    /// <param name="grant">The user's grant override for the feature.</param>
    /// <param name="deny">The user's deny override for the feature.</param>
    /// <returns>False when denied (deny wins), true when granted, null when inherited.</returns>
    public static bool? CellOf(OperationFlags flag, OperationFlags grant, OperationFlags deny)
        => (deny & flag) == flag ? false : (grant & flag) == flag ? true : null;

    /// <summary>The grant and deny overrides the cells describe, limited to the operations the feature offers.</summary>
    /// <param name="cells">The state of each column, in <see cref="Columns"/> order.</param>
    /// <param name="available">The operations the feature offers.</param>
    /// <returns>The grant and deny flags.</returns>
    public static (OperationFlags Grant, OperationFlags Deny) FromCells(IReadOnlyList<bool?> cells, OperationFlags available)
    {
        OperationFlags grant = OperationFlags.None, deny = OperationFlags.None;
        for (int i = 0; i < Columns.Length && i < cells.Count; i++)
        {
            if (cells[i] == true)
            {
                grant |= Columns[i];
            }
            else if (cells[i] == false)
            {
                deny |= Columns[i];
            }
        }

        return (grant & available, deny & available);
    }

    /// <summary>The operations the user ends up with: the roles' operations plus grants, less denials.</summary>
    /// <param name="inherited">The operations the user's roles give.</param>
    /// <param name="grant">The grant override.</param>
    /// <param name="deny">The deny override.</param>
    /// <returns>The effective operations.</returns>
    public static OperationFlags Effective(OperationFlags inherited, OperationFlags grant, OperationFlags deny) => (inherited | grant) & ~deny;
}
