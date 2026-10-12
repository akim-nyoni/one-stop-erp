namespace One4All.Infrastructure.Persistence;

/// <summary>
/// The signed-in user's row scope for a host with one user (the desktop): every database context created afterwards reads only the rows that
/// user's scope rules allow (brief §5.1) — payroll and HR by branch and department, stock by warehouse, and so on. The server applies the scope
/// per request instead and leaves this empty.
/// </summary>
public sealed class RowScopeHolder
{
    /// <summary>Gets or sets the scope applied to new contexts (null: none).</summary>
    public Application.Security.RowScope? Scope { get; set; }
}
