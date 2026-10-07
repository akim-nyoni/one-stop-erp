using One4All.Application.Abstractions.Security;
using One4All.Core.Security;

namespace One4All.Desktop;

/// <summary>
/// The desktop's identity for every service scope: the signed-in session's user on this machine, so the audit and login trails record who
/// did what and from where (brief §5.2, §6).
/// </summary>
/// <param name="session">The signed-in session.</param>
public sealed class DesktopCurrentUser(DesktopSession session) : ICurrentUser
{
    /// <inheritdoc />
    public long? UserId => session.UserId;

    /// <inheritdoc />
    public string UserName => session.UserName ?? "System";

    /// <inheritdoc />
    public long? CompanyId => null;

    /// <inheritdoc />
    public bool IsAuthenticated => session.UserId is not null;

    /// <inheritdoc />
    public EffectivePermissions Permissions => EffectivePermissions.None;

    /// <inheritdoc />
    public string? Terminal => Environment.MachineName;

    /// <inheritdoc />
    public string? ClientAddress => Environment.MachineName;
}
