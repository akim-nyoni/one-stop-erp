using One4All.Core.Security;

namespace One4All.Application.Security;

/// <summary>
/// The data a user may reach (brief §5.1 row-level scopes): each role assignment may restrict its role to listed companies, branches,
/// warehouses, tills, cost centres, departments, wards or jobs. A value is reachable on a dimension when any assignment leaves that
/// dimension open or lists it. Pure.
/// </summary>
public sealed class DataScope
{
    private readonly IReadOnlyList<IReadOnlyDictionary<ScopeDimension, IReadOnlyList<string>>> _assignments;
    private readonly bool _unrestricted;

    private DataScope(IReadOnlyList<IReadOnlyDictionary<ScopeDimension, IReadOnlyList<string>>> assignments, bool unrestricted)
    {
        _assignments = assignments;
        _unrestricted = unrestricted;
    }

    /// <summary>A scope that reaches everything (system administrators, hosts acting for no user).</summary>
    public static DataScope Everything { get; } = new([], true);

    /// <summary>Builds the scope from a user's role assignments.</summary>
    /// <param name="assignments">Each assignment's scopes by dimension.</param>
    /// <param name="isSuperAdministrator">Whether the user is a system administrator.</param>
    /// <returns>The scope.</returns>
    public static DataScope From(IEnumerable<IReadOnlyDictionary<ScopeDimension, IReadOnlyList<string>>> assignments, bool isSuperAdministrator)
    {
        List<IReadOnlyDictionary<ScopeDimension, IReadOnlyList<string>>> list = [.. assignments];
        return new DataScope(list, isSuperAdministrator || list.Count == 0);
    }

    /// <summary>Whether a dimension is restricted at all for this user.</summary>
    /// <param name="dimension">The dimension.</param>
    /// <returns>True when some value would be refused.</returns>
    public bool Restricts(ScopeDimension dimension) => !_unrestricted && _assignments.All(a => a.ContainsKey(dimension));

    /// <summary>The keys (codes or ids) allowed on a dimension, or null when it is not restricted.</summary>
    /// <param name="dimension">The dimension.</param>
    /// <returns>The allowed keys, trimmed and distinct (ignoring case), or null.</returns>
    public IReadOnlyList<string>? AllowedValues(ScopeDimension dimension)
        => Restricts(dimension)
            ? [.. _assignments.SelectMany(a => a.TryGetValue(dimension, out IReadOnlyList<string>? values) ? values : []).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)]
            : null;

    /// <summary>Whether a record identified by any of its keys (code, id) is reachable on a dimension.</summary>
    /// <param name="dimension">The dimension.</param>
    /// <param name="keys">The record's code and/or id.</param>
    /// <returns>True when reachable.</returns>
    public bool Allows(ScopeDimension dimension, params string?[] keys)
        => !Restricts(dimension)
            || _assignments.Any(a => a.TryGetValue(dimension, out IReadOnlyList<string>? values) && keys.OfType<string>().Any(k => values.Contains(k.Trim(), StringComparer.OrdinalIgnoreCase)));
}
