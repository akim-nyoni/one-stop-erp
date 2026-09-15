using One4All.Core.Common;

namespace One4All.Core.Reporting;

/// <summary>The scope/precedence of a stored report definition (brief §13.2 resolution order).</summary>
public enum ReportScope
{
    /// <summary>A shipped system report (read-only; copy to a company/user version to edit).</summary>
    System = 0,

    /// <summary>A company-level customisation.</summary>
    Company = 1,

    /// <summary>A user-level customisation.</summary>
    User = 2,
}

/// <summary>
/// A stored report definition (<c>rpt.report_definition</c>, brief §13.1). The banded layout is held as
/// <c>.mrpt</c> JSON in <see cref="DefinitionJson"/>. At run time the definition for a code is resolved in
/// precedence order user → company → system, so a customer's version overrides the shipped one without
/// being clobbered on upgrade.
/// </summary>
public sealed class ReportDefinitionRecord : AuditableEntity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ReportDefinitionRecord()
    {
        Code = string.Empty;
        Name = string.Empty;
        DefinitionJson = string.Empty;
    }

    /// <summary>Creates a stored report definition.</summary>
    /// <param name="code">The report code.</param>
    /// <param name="name">The display name.</param>
    /// <param name="definitionJson">The <c>.mrpt</c> JSON.</param>
    /// <param name="scope">The scope/precedence.</param>
    /// <param name="companyId">The company (null for system reports).</param>
    /// <param name="ownerUserId">The owning user (for user versions).</param>
    /// <param name="module">The owning module.</param>
    public ReportDefinitionRecord(string code, string name, string definitionJson, ReportScope scope, long? companyId = null, long? ownerUserId = null, string? module = null)
    {
        Code = code;
        Name = name;
        DefinitionJson = definitionJson;
        Scope = scope;
        CompanyId = companyId;
        OwnerUserId = ownerUserId;
        Module = module;
    }

    /// <summary>The report code (stable identifier).</summary>
    public string Code { get; private set; }

    /// <summary>The display name.</summary>
    public string Name { get; set; }

    /// <summary>The owning module.</summary>
    public string? Module { get; set; }

    /// <summary>The scope/precedence.</summary>
    public ReportScope Scope { get; private set; }

    /// <summary>The company this definition belongs to (null for system reports).</summary>
    public long? CompanyId { get; private set; }

    /// <summary>The owning user (for user versions).</summary>
    public long? OwnerUserId { get; private set; }

    /// <summary>The definition version.</summary>
    public int Version { get; private set; } = 1;

    /// <summary>The <c>.mrpt</c> JSON of the banded layout.</summary>
    public string DefinitionJson { get; private set; }

    /// <summary>Replaces the definition JSON, bumping the version.</summary>
    /// <param name="definitionJson">The new JSON.</param>
    public void UpdateDefinition(string definitionJson)
    {
        DefinitionJson = definitionJson;
        Version++;
    }
}
