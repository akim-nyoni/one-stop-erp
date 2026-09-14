namespace One4All.Core.Security;

/// <summary>
/// A single entry in the permission catalogue: a <c>Module.Feature</c> permission together with the
/// operation flags that are <em>available</em> for it (the applicable grid columns) and the licensing
/// module that gates it. The catalogue (<see cref="PermissionCatalogue"/>) is the source of truth in
/// code and is seeded into <c>sec.permission</c>.
/// </summary>
/// <param name="Code">The stable permission code, <c>Module.Feature</c> (e.g. <c>Housing.Stand</c>).</param>
/// <param name="Module">The owning module grouping used to build the permission tree (e.g. <c>Housing</c>).</param>
/// <param name="Feature">The feature name within the module (e.g. <c>Stand</c>).</param>
/// <param name="Name">Human-readable display name for the grid row.</param>
/// <param name="Available">The operation flags applicable to this feature.</param>
/// <param name="LicenceModule">The licensing module code that must be licensed for this permission to be usable (e.g. <c>HSE</c>).</param>
public sealed record PermissionDefinition(
    string Code,
    string Module,
    string Feature,
    string Name,
    OperationFlags Available,
    string LicenceModule)
{
    /// <summary>The standard flag set for master-data maintenance features.</summary>
    public const OperationFlags MasterData =
        OperationFlags.View | OperationFlags.Create | OperationFlags.Edit |
        OperationFlags.Delete | OperationFlags.Print | OperationFlags.Export;

    /// <summary>The standard flag set for transactional documents that post and can be reversed/approved.</summary>
    public const OperationFlags Transaction =
        OperationFlags.View | OperationFlags.Create | OperationFlags.Edit | OperationFlags.Print |
        OperationFlags.Export | OperationFlags.Post | OperationFlags.Approve | OperationFlags.VoidReverse;

    /// <summary>Read-only features (enquiries, reports).</summary>
    public const OperationFlags ReadOnly = OperationFlags.View | OperationFlags.Print | OperationFlags.Export;

    /// <summary>The five authorisation levels, OR-ed together, for features that route through the approval engine.</summary>
    public const OperationFlags AllAuthoriseLevels =
        OperationFlags.AuthoriseLevel1 | OperationFlags.AuthoriseLevel2 | OperationFlags.AuthoriseLevel3 |
        OperationFlags.AuthoriseLevel4 | OperationFlags.AuthoriseLevel5;
}
