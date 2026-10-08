using One4All.Core.Common;

namespace One4All.Core.Security;

/// <summary>
/// A machine client of the API (<c>sec.api_client</c>, brief §10: "API keys / client credentials for integrations"): it signs in with its client id and
/// secret instead of a user's password and acts with the permissions of the service user it names. Only the secret's SHA-256 is kept.
/// </summary>
public sealed class ApiClient : AuditableEntity
{
    private ApiClient()
    {
        Name = string.Empty;
        ClientId = string.Empty;
        SecretHash = string.Empty;
    }

    /// <summary>Registers a client.</summary>
    /// <param name="name">What the client is.</param>
    /// <param name="clientId">Its public id.</param>
    /// <param name="secretHash">Its secret's SHA-256 (hex).</param>
    /// <param name="actsAsUserId">The service user whose permissions it has.</param>
    /// <param name="expiresOn">When it stops working (null: until revoked).</param>
    /// <param name="requestsPerMinute">Its own rate limit (null: the server's).</param>
    public ApiClient(string name, string clientId, string secretHash, long actsAsUserId, DateOnly? expiresOn, int? requestsPerMinute)
    {
        Name = name;
        ClientId = clientId;
        SecretHash = secretHash;
        ActsAsUserId = actsAsUserId;
        ExpiresOn = expiresOn;
        RequestsPerMinute = requestsPerMinute;
        IsActive = true;
    }

    /// <summary>Gets what the client is.</summary>
    public string Name { get; private set; }

    /// <summary>Gets its public id.</summary>
    public string ClientId { get; private set; }

    /// <summary>Gets its secret's SHA-256 (hex).</summary>
    public string SecretHash { get; private set; }

    /// <summary>Gets the service user whose permissions it has.</summary>
    public long ActsAsUserId { get; private set; }

    /// <summary>Gets when it stops working.</summary>
    public DateOnly? ExpiresOn { get; private set; }

    /// <summary>Gets its own rate limit per minute.</summary>
    public int? RequestsPerMinute { get; private set; }

    /// <summary>Gets or sets a value indicating whether it may sign in.</summary>
    public bool IsActive { get; set; }

    /// <summary>Gets or sets when it last signed in.</summary>
    public DateTimeOffset? LastUsedOnUtc { get; set; }

    /// <summary>Replaces the secret (the old one stops working).</summary>
    /// <param name="secretHash">The new secret's SHA-256.</param>
    public void Rotate(string secretHash) => SecretHash = secretHash;
}
