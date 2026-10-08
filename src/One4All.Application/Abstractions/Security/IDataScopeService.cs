using One4All.Application.Security;
using One4All.Core.Security;

namespace One4All.Application.Abstractions.Security;

/// <summary>Resolves and checks a user's row-level data scopes (brief §5.1).</summary>
public interface IDataScopeService
{
    /// <summary>The user's data scope.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The scope.</returns>
    Task<DataScope> ForUserAsync(long userId, CancellationToken ct = default);

    /// <summary>
    /// Whether the user may reach a record by its id on a dimension (company, branch, warehouse, till, cost centre, department or job —
    /// scopes may list either the record's code or its id). Wards are matched by code only.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="dimension">The dimension.</param>
    /// <param name="id">The record id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when reachable.</returns>
    Task<bool> AllowsAsync(long userId, ScopeDimension dimension, long id, CancellationToken ct = default);

    /// <summary>The rows a user's scope rules reach, for the database's row filters: allowed codes resolved to ids where rows hold ids.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The row scope (<see cref="RowScope.None"/> for an unrestricted user).</returns>
    Task<RowScope> RowScopeAsync(long userId, CancellationToken ct = default);
}
