using One4All.Core.Common;

namespace One4All.Core.Licensing;

/// <summary>
/// The licence token currently installed in this deployment (<c>lic.installed_licence</c>). At most one
/// row is active; installing a new licence deactivates the previous one, keeping a history. The token is
/// validated offline on every load - nothing here is trusted without a signature check.
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

/// <summary>A vendor revocation list installed on this system (<c>lic.installed_revocation_list</c>); the highest sequence applies.</summary>
public sealed class InstalledRevocationList : AuditableEntity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private InstalledRevocationList() => Token = string.Empty;

    /// <summary>Records an installed list.</summary>
    /// <param name="token">The signed token.</param>
    /// <param name="sequence">Its sequence number.</param>
    /// <param name="revokedCount">How many licences it revokes.</param>
    /// <param name="installedOnUtc">When.</param>
    /// <param name="installedByUserId">Who.</param>
    public InstalledRevocationList(string token, int sequence, int revokedCount, DateTimeOffset installedOnUtc, long? installedByUserId)
    {
        Token = token;
        Sequence = sequence;
        RevokedCount = revokedCount;
        InstalledOnUtc = installedOnUtc;
        InstalledByUserId = installedByUserId;
    }

    /// <summary>Gets the signed token.</summary>
    public string Token { get; private set; }

    /// <summary>Gets the list's sequence number.</summary>
    public int Sequence { get; private set; }

    /// <summary>Gets how many licences it revokes.</summary>
    public int RevokedCount { get; private set; }

    /// <summary>Gets when it was installed.</summary>
    public DateTimeOffset InstalledOnUtc { get; private set; }

    /// <summary>Gets who installed it.</summary>
    public long? InstalledByUserId { get; private set; }
}
