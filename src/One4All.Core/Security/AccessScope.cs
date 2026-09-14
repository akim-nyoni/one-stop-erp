using One4All.Core.Common;

namespace One4All.Core.Security;

/// <summary>
/// A row-level security restriction on a role assignment (<c>sec.access_scope</c>): the principal,
/// through this assignment, may only touch data whose <see cref="Dimension"/> value is in
/// <see cref="Values"/>. An assignment with no scope for a dimension is unrestricted on that dimension.
/// </summary>
public sealed class AccessScope : Entity
{
    private readonly List<string> _values = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private AccessScope()
    {
    }

    /// <summary>Creates a scope.</summary>
    /// <param name="dimension">The dimension restricted.</param>
    /// <param name="values">The allowed values on that dimension.</param>
    public AccessScope(ScopeDimension dimension, IEnumerable<string> values)
    {
        Dimension = dimension;
        _values.AddRange(values.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>The owning user-role assignment id.</summary>
    public long UserRoleId { get; private set; }

    /// <summary>The restricted dimension.</summary>
    public ScopeDimension Dimension { get; private set; }

    /// <summary>The allowed values on the dimension (e.g. branch codes, ward numbers).</summary>
    public IReadOnlyList<string> Values => _values.AsReadOnly();

    /// <summary>Returns true when <paramref name="value"/> is allowed by this scope.</summary>
    /// <param name="value">The candidate dimension value.</param>
    /// <returns>True when in range.</returns>
    public bool Allows(string value) => _values.Contains(value, StringComparer.OrdinalIgnoreCase);
}
