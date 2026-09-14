using One4All.Core.Common;

namespace One4All.Core.Licensing;

/// <summary>
/// The licence token currently installed in this deployment (<c>lic.installed_licence</c>). At most one
/// row is active; installing a new licence deactivates the previous one, keeping a history. The token is
/// validated offline on every load — nothing here is trusted without a signature check.
/// </summary>
public sealed class InstalledLicence : AuditableEntity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private InstalledLicence() => Token = string.Empty;

    /// <summary>Records a newly installed licence token.</summary>
    /// <param name="token">The compact licence token.</param>
    /// <param name="installedOnUtc">When it was installed.</param>
    /// <param name="installedByUserId">Who installed it.</param>
    public InstalledLicence(string token, DateTimeOffset installedOnUtc, long? installedByUserId)
    {
        Token = token;
        InstalledOnUtc = installedOnUtc;
        InstalledByUserId = installedByUserId;
        IsActive = true;
    }

    /// <summary>The compact licence token (<c>payload.signature</c>).</summary>
    public string Token { get; private set; }

    /// <summary>When the licence was installed.</summary>
    public DateTimeOffset InstalledOnUtc { get; private set; }

    /// <summary>Who installed the licence.</summary>
    public long? InstalledByUserId { get; private set; }

    /// <summary>Whether this is the active licence.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Deactivates this licence (superseded by a newer install).</summary>
    public void Deactivate() => IsActive = false;
}
