using One4All.Core.Common;

namespace One4All.Core.Security;

/// <summary>
/// A segregation-of-duties rule kept in the rules table (<c>sec.sod_rule</c>): a new conflict, or a built-in rule of the same code re-tuned or
/// switched off.
/// </summary>
public sealed class SodRuleSetting : AuditableEntity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SodRuleSetting()
    {
        Code = string.Empty;
        Name = string.Empty;
        PermissionA = string.Empty;
        PermissionB = string.Empty;
    }

    /// <summary>Creates a rule.</summary>
    /// <param name="code">The code.</param>
    public SodRuleSetting(string code)
        : this()
    {
        Code = code.Trim().ToUpperInvariant();
    }

    /// <summary>Gets the code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the first permission.</summary>
    public string PermissionA { get; set; }

    /// <summary>Gets or sets the first permission's operations.</summary>
    public OperationFlags OperationA { get; set; }

    /// <summary>Gets or sets the conflicting permission.</summary>
    public string PermissionB { get; set; }

    /// <summary>Gets or sets the conflicting permission's operations.</summary>
    public OperationFlags OperationB { get; set; }

    /// <summary>Gets or sets the severity.</summary>
    public SodSeverity Severity { get; set; }

    /// <summary>Gets or sets whether the rule is in force (false switches a built-in rule of the same code off).</summary>
    public bool IsActive { get; set; }

    /// <summary>The rule.</summary>
    /// <returns>The rule.</returns>
    public SodRule ToRule() => new(Code, Name, PermissionA, OperationA, PermissionB, OperationB, Severity);
}
