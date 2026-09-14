namespace One4All.Core.Security;

/// <summary>
/// The operation flags that form the columns of the permission grid (brief §5.1). A
/// <see cref="Permission"/> declares which flags are <em>available</em> for a feature; a role or
/// user grant then holds the <em>granted</em> subset. Backed by <see cref="long"/> to leave head-room
/// for module-specific flags without widening later.
/// </summary>
[Flags]
public enum OperationFlags : long
{
    /// <summary>No operations permitted.</summary>
    None = 0,

    /// <summary>See the record/list.</summary>
    View = 1L << 0,

    /// <summary>Create a new record.</summary>
    Create = 1L << 1,

    /// <summary>Edit an existing record.</summary>
    Edit = 1L << 2,

    /// <summary>Delete (soft-delete for master data).</summary>
    Delete = 1L << 3,

    /// <summary>Print a document/report.</summary>
    Print = 1L << 4,

    /// <summary>Export data (CSV/Excel/PDF).</summary>
    Export = 1L << 5,

    /// <summary>Post a transaction to the ledger.</summary>
    Post = 1L << 6,

    /// <summary>Approve a document in a workflow.</summary>
    Approve = 1L << 7,

    /// <summary>Void or reverse a posted transaction.</summary>
    VoidReverse = 1L << 8,

    /// <summary>Authorisation level 1 (lowest band).</summary>
    AuthoriseLevel1 = 1L << 9,

    /// <summary>Authorisation level 2.</summary>
    AuthoriseLevel2 = 1L << 10,

    /// <summary>Authorisation level 3.</summary>
    AuthoriseLevel3 = 1L << 11,

    /// <summary>Authorisation level 4.</summary>
    AuthoriseLevel4 = 1L << 12,

    /// <summary>Authorisation level 5 (highest band, e.g. land acquisition, allocation approval).</summary>
    AuthoriseLevel5 = 1L << 13,

    /// <summary>Override a price at point of entry/sale.</summary>
    OverridePrice = 1L << 14,

    /// <summary>Override a discount beyond the standard limit.</summary>
    OverrideDiscount = 1L << 15,

    /// <summary>Override a customer credit limit.</summary>
    OverrideCreditLimit = 1L << 16,

    /// <summary>View cost prices (field-level sensitivity).</summary>
    ViewCost = 1L << 17,

    /// <summary>View margin (field-level sensitivity).</summary>
    ViewMargin = 1L << 18,

    /// <summary>Reprint a document (audited).</summary>
    Reprint = 1L << 19,

    /// <summary>Post/enter with a back-dated accounting date.</summary>
    BackDate = 1L << 20,
}

/// <summary>Helpers for working with <see cref="OperationFlags"/>.</summary>
public static class OperationFlagsExtensions
{
    /// <summary>The five authorisation-level flags, in ascending order.</summary>
    public static readonly OperationFlags[] AuthoriseLevels =
    [
        OperationFlags.AuthoriseLevel1,
        OperationFlags.AuthoriseLevel2,
        OperationFlags.AuthoriseLevel3,
        OperationFlags.AuthoriseLevel4,
        OperationFlags.AuthoriseLevel5,
    ];

    /// <summary>Returns true when <paramref name="granted"/> includes every flag in <paramref name="required"/>.</summary>
    /// <param name="granted">The flags a principal holds.</param>
    /// <param name="required">The flags an operation needs.</param>
    /// <returns>True when all required flags are present.</returns>
    public static bool Includes(this OperationFlags granted, OperationFlags required) =>
        (granted & required) == required;

    /// <summary>Enumerates the individual set flags (excluding <see cref="OperationFlags.None"/>).</summary>
    /// <param name="flags">The flag set.</param>
    /// <returns>Each individual flag that is set.</returns>
    public static IEnumerable<OperationFlags> Enumerate(this OperationFlags flags)
    {
        foreach (OperationFlags value in Enum.GetValues<OperationFlags>())
        {
            if (value != OperationFlags.None && (flags & value) == value)
            {
                yield return value;
            }
        }
    }
}
