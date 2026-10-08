namespace One4All.Application.Security;

/// <summary>
/// The rows a user's scope rules reach, ready for the database's row filters (brief §5.1 scope rules): per restricted dimension, the ids
/// (branches, warehouses, tills, jobs) or upper-case codes (cost centres, departments, wards/zones) allowed. A null list leaves that dimension
/// unfiltered; a row with no value on a dimension (untagged) stays visible.
/// </summary>
/// <param name="BranchIds">Allowed branches.</param>
/// <param name="WarehouseIds">Allowed warehouses.</param>
/// <param name="TillIds">Allowed tills.</param>
/// <param name="JobIds">Allowed jobs / contracts.</param>
/// <param name="CostCentres">Allowed cost centre codes.</param>
/// <param name="Departments">Allowed department codes.</param>
/// <param name="Wards">Allowed ward / zone codes.</param>
public sealed record RowScope(long[]? BranchIds, long[]? WarehouseIds, long[]? TillIds, long[]? JobIds, string[]? CostCentres, string[]? Departments, string[]? Wards)
{
    /// <summary>Gets a scope filtering nothing.</summary>
    public static RowScope None { get; } = new(null, null, null, null, null, null, null);

    /// <summary>Gets a value indicating whether nothing is filtered.</summary>
    public bool IsEmpty => BranchIds is null && WarehouseIds is null && TillIds is null && JobIds is null && CostCentres is null && Departments is null && Wards is null;
}
