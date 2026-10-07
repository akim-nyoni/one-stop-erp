using One4All.Application.Abstractions.Security;
using One4All.Core.Security;

namespace One4All.Infrastructure.Security;

/// <summary>
/// A mutable <see cref="ICurrentUser"/> populated after authentication (embedded desktop) or per request
/// (API middleware). Defaults to an unauthenticated "System" principal, which is what seeding and
/// migrations run as.
/// </summary>
public sealed class CurrentUserContext : ICurrentUser
{
    /// <inheritdoc />
    public long? UserId { get; private set; }

    /// <inheritdoc />
    public string UserName { get; private set; } = "System";

    /// <inheritdoc />
    public long? CompanyId { get; private set; }

    /// <inheritdoc />
    public string? Terminal { get; private set; }

    /// <inheritdoc />
    public EffectivePermissions Permissions { get; private set; } = EffectivePermissions.None;

    /// <inheritdoc />
    public bool IsAuthenticated => UserId is not null;

    /// <summary>Sets the signed-in principal.</summary>
    /// <param name="userId">User id.</param>
    /// <param name="userName">User name.</param>
    /// <param name="permissions">Resolved permissions.</param>
    /// <param name="companyId">Active company id.</param>
    /// <param name="terminal">Terminal identifier.</param>
    public void SignIn(long userId, string userName, EffectivePermissions permissions, long? companyId = null, string? terminal = null)
    {
        UserId = userId;
        UserName = userName;
        Permissions = permissions;
        CompanyId = companyId;
        Terminal = terminal;
    }

    /// <inheritdoc />
    public string? ClientAddress { get; private set; }

    /// <summary>Records where the request comes from (an IP address or machine name).</summary>
    /// <param name="address">The address.</param>
    public void SetClientAddress(string? address) => ClientAddress = string.IsNullOrWhiteSpace(address) ? null : address.Trim();

    /// <summary>Switches the active company for the signed-in principal.</summary>
    /// <param name="companyId">The company id.</param>
    public void SetCompany(long? companyId) => CompanyId = companyId;

    /// <summary>Clears the principal back to the unauthenticated System context.</summary>
    public void SignOut()
    {
        UserId = null;
        UserName = "System";
        CompanyId = null;
        Terminal = null;
        Permissions = EffectivePermissions.None;
    }
}
