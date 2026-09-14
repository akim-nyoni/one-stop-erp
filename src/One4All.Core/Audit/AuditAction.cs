namespace One4All.Core.Audit;

/// <summary>
/// The kinds of action recorded in the change audit log (brief §6B). Covers both data changes and
/// non-entity events (logins, prints, exports, overrides, configuration and permission changes).
/// </summary>
public enum AuditAction
{
    /// <summary>A row was inserted.</summary>
    Insert = 1,

    /// <summary>A row was updated.</summary>
    Update = 2,

    /// <summary>A row was deleted (soft or hard).</summary>
    Delete = 3,

    /// <summary>A user signed in.</summary>
    Login = 4,

    /// <summary>A user signed out.</summary>
    Logout = 5,

    /// <summary>A document or report was printed.</summary>
    Print = 6,

    /// <summary>Data was exported.</summary>
    Export = 7,

    /// <summary>A transaction was posted.</summary>
    Post = 8,

    /// <summary>A document was approved.</summary>
    Approve = 9,

    /// <summary>A document was rejected.</summary>
    Reject = 10,

    /// <summary>A transaction was voided/reversed.</summary>
    Void = 11,

    /// <summary>A supervisor override was performed.</summary>
    Override = 12,

    /// <summary>A configuration value changed.</summary>
    ConfigChange = 13,

    /// <summary>A permission or role assignment changed.</summary>
    PermissionChange = 14,

    /// <summary>A licence was installed or changed.</summary>
    LicenceChange = 15,
}
