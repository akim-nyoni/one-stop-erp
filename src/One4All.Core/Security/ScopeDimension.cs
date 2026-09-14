namespace One4All.Core.Security;

/// <summary>
/// The dimensions a role assignment can be scoped to for row-level security (brief §5.1). A scope
/// restricts what data a principal sees; enforced server-side in query filters, never only in the UI.
/// </summary>
public enum ScopeDimension
{
    /// <summary>Restrict to specific companies.</summary>
    Company = 1,

    /// <summary>Restrict to specific branches / sites.</summary>
    Branch = 2,

    /// <summary>Restrict to specific warehouses.</summary>
    Warehouse = 3,

    /// <summary>Restrict to specific tills / POS terminals.</summary>
    Till = 4,

    /// <summary>Restrict to specific cost centres.</summary>
    CostCentre = 5,

    /// <summary>Restrict to specific departments.</summary>
    Department = 6,

    /// <summary>Restrict to specific wards / zones (municipal / housing).</summary>
    WardZone = 7,

    /// <summary>Restrict to specific jobs / projects.</summary>
    Job = 8,
}
