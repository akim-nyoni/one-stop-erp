namespace One4All.Core.Common;

/// <summary>
/// Marks an entity as belonging to a single company. The persistence layer applies a global query
/// filter on <see cref="CompanyId"/> (brief §2.4) and the API re-checks it, so cross-company data
/// never leaks through a forgotten <c>WHERE</c> clause.
/// </summary>
public interface ICompanyScoped
{
    /// <summary>The owning company's identity.</summary>
    long CompanyId { get; }
}
