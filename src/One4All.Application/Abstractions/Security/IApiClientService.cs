using One4All.Core.Common;

namespace One4All.Application.Abstractions.Security;

/// <summary>A newly issued client credential: the secret is shown this once and never again.</summary>
/// <param name="ClientId">The public client id.</param>
/// <param name="Secret">The secret.</param>
/// <param name="ApiKey">The value to send in the <c>X-Api-Key</c> header (<c>{ClientId}.{Secret}</c>).</param>
public sealed record ApiCredential(string ClientId, string Secret, string ApiKey);

/// <summary>A signed-in API client.</summary>
/// <param name="ClientId">The client id.</param>
/// <param name="UserId">The service user it acts as.</param>
/// <param name="UserName">That user's name.</param>
/// <param name="RequestsPerMinute">Its own rate limit (null: the server's).</param>
public sealed record ApiClientIdentity(string ClientId, long UserId, string UserName, int? RequestsPerMinute);

/// <summary>A registered client, as listed.</summary>
/// <param name="ClientId">The client id.</param>
/// <param name="Name">What it is.</param>
/// <param name="ActsAs">The user it acts as.</param>
/// <param name="IsActive">Whether it may sign in.</param>
/// <param name="ExpiresOn">When it stops working.</param>
/// <param name="LastUsedOnUtc">When it last signed in.</param>
/// <param name="RequestsPerMinute">Its rate limit.</param>
public sealed record ApiClientRow(string ClientId, string Name, string ActsAs, bool IsActive, DateOnly? ExpiresOn, DateTimeOffset? LastUsedOnUtc, int? RequestsPerMinute);

/// <summary>
/// API keys for integrations (brief §10): a client is issued an id and secret, acts as a named service user (so the same permissions and data scopes apply),
/// can carry its own rate limit and expiry, and is revoked or rotated without touching anyone's password.
/// </summary>
public interface IApiClientService
{
    /// <summary>Registers a client.</summary>
    /// <param name="name">What it is.</param>
    /// <param name="actsAsUserName">The service user it acts as.</param>
    /// <param name="expiresOn">When it stops working (null: until revoked).</param>
    /// <param name="requestsPerMinute">Its own rate limit (null: the server's).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The credential (its secret is shown only now).</returns>
    Task<Result<ApiCredential>> CreateAsync(string name, string actsAsUserName, DateOnly? expiresOn, int? requestsPerMinute, CancellationToken ct = default);

    /// <summary>Issues a new secret; the old one stops working.</summary>
    /// <param name="clientId">The client.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new credential.</returns>
    Task<Result<ApiCredential>> RotateAsync(string clientId, CancellationToken ct = default);

    /// <summary>Stops a client signing in.</summary>
    /// <param name="clientId">The client.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or why not.</returns>
    Task<Result> RevokeAsync(string clientId, CancellationToken ct = default);

    /// <summary>The registered clients.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The clients.</returns>
    Task<IReadOnlyList<ApiClientRow>> ListAsync(CancellationToken ct = default);

    /// <summary>Checks an <c>X-Api-Key</c> value: an active, unexpired client with that secret, acting as an active user.</summary>
    /// <param name="apiKey">The key (<c>{clientId}.{secret}</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Who it is, or why not.</returns>
    Task<Result<ApiClientIdentity>> AuthenticateAsync(string apiKey, CancellationToken ct = default);
}
