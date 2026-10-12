namespace One4All.Core.Security;

/// <summary>Where two roles differ on one permission.</summary>
/// <param name="Code">The permission code.</param>
/// <param name="OnlyFirst">What only the first role grants.</param>
/// <param name="OnlySecond">What only the second role grants.</param>
/// <param name="Both">What both grant.</param>
public sealed record RoleDifference(string Code, OperationFlags OnlyFirst, OperationFlags OnlySecond, OperationFlags Both);

/// <summary>How a bulk change applies its operations.</summary>
public enum BulkGrantMode
{
    /// <summary>Adds the operations to what each role already has.</summary>
    Add = 0,

    /// <summary>Takes the operations away (a grant left with none is removed).</summary>
    Remove = 1,

    /// <summary>Sets exactly these operations.</summary>
    Set = 2,
}

/// <summary>
/// The permission grid's compare and bulk tools (brief §6 permission grid: copy, compare and bulk change): the differences between two
/// roles, the codes a pattern such as <c>Payroll.*</c> covers, and the operations a bulk change leaves. Pure and unit-tested.
/// </summary>
public static class RoleComparison
{
    /// <summary>The permissions on which two roles differ (codes both grant identically are left out), by code.</summary>
    /// <param name="first">The first role's grants.</param>
    /// <param name="second">The second role's grants.</param>
    /// <returns>The differences.</returns>
    public static IReadOnlyList<RoleDifference> Compare(IReadOnlyDictionary<string, OperationFlags> first, IReadOnlyDictionary<string, OperationFlags> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        Dictionary<string, OperationFlags> a = new(first, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, OperationFlags> b = new(second, StringComparer.OrdinalIgnoreCase);
        List<RoleDifference> differences = [];
        foreach (string code in a.Keys.Union(b.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            OperationFlags x = a.GetValueOrDefault(code), y = b.GetValueOrDefault(code);
            if (x != y)
            {
                differences.Add(new RoleDifference(code, x & ~y, y & ~x, x & y));
            }
        }

        return differences;
    }

    /// <summary>The permission codes a pattern covers: an exact code, or a prefix ending in <c>*</c> (e.g. <c>Payroll.*</c>, or <c>*</c> for all).</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="known">The codes to match against.</param>
    /// <returns>The matching codes.</returns>
    public static IReadOnlyList<string> Matching(string pattern, IEnumerable<string> known)
    {
        string p = (pattern ?? string.Empty).Trim();
        if (p.Length == 0)
        {
            return [];
        }

        if (!p.EndsWith('*'))
        {
            return [.. known.Where(k => string.Equals(k, p, StringComparison.OrdinalIgnoreCase)).DefaultIfEmpty(p).Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        string prefix = p[..^1];
        return [.. known.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>What a grant becomes under a bulk change.</summary>
    /// <param name="current">What the role grants now.</param>
    /// <param name="operations">The operations of the change.</param>
    /// <param name="mode">Add, remove or set.</param>
    /// <returns>The new operations (None removes the grant).</returns>
    public static OperationFlags Apply(OperationFlags current, OperationFlags operations, BulkGrantMode mode) => mode switch
    {
        BulkGrantMode.Remove => current & ~operations,
        BulkGrantMode.Set => operations,
        _ => current | operations,
    };
}
