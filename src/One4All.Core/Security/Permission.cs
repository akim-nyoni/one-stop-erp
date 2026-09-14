using One4All.Core.Common;

namespace One4All.Core.Security;

/// <summary>
/// The persisted mirror of a <see cref="PermissionDefinition"/> catalogue entry (<c>sec.permission</c>).
/// Seeded from <see cref="PermissionCatalogue"/> and referenced by role grants and user overrides.
/// </summary>
public sealed class Permission : AuditableEntity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Permission()
    {
        Code = string.Empty;
        Module = string.Empty;
        Feature = string.Empty;
        Name = string.Empty;
        LicenceModule = string.Empty;
    }

    /// <summary>Creates a permission from a catalogue definition.</summary>
    /// <param name="definition">The catalogue definition.</param>
    public Permission(PermissionDefinition definition)
    {
        Code = definition.Code;
        Module = definition.Module;
        Feature = definition.Feature;
        Name = definition.Name;
        AvailableOperations = definition.Available;
        LicenceModule = definition.LicenceModule;
    }

    /// <summary>The stable <c>Module.Feature</c> code.</summary>
    public string Code { get; private set; }

    /// <summary>The module grouping used to build the permission tree.</summary>
    public string Module { get; private set; }

    /// <summary>The feature name within the module.</summary>
    public string Feature { get; private set; }

    /// <summary>The display name for the grid row.</summary>
    public string Name { get; private set; }

    /// <summary>The operations applicable to this feature (the available grid columns).</summary>
    public OperationFlags AvailableOperations { get; private set; }

    /// <summary>The licensing module that gates this permission.</summary>
    public string LicenceModule { get; private set; }

    /// <summary>Returns true when this row already matches the catalogue definition (no update needed).</summary>
    /// <param name="definition">The catalogue definition.</param>
    /// <returns>True when name, available operations, module, feature and licence module all match.</returns>
    public bool MatchesDefinition(PermissionDefinition definition) =>
        Name == definition.Name &&
        AvailableOperations == definition.Available &&
        Module == definition.Module &&
        Feature == definition.Feature &&
        LicenceModule == definition.LicenceModule;

    /// <summary>Updates the mutable metadata of this permission from the catalogue definition.</summary>
    /// <param name="definition">The catalogue definition (its <see cref="PermissionDefinition.Code"/> must equal <see cref="Code"/>).</param>
    public void SyncWith(PermissionDefinition definition)
    {
        Module = definition.Module;
        Feature = definition.Feature;
        Name = definition.Name;
        AvailableOperations = definition.Available;
        LicenceModule = definition.LicenceModule;
    }
}
