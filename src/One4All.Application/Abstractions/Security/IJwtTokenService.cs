namespace One4All.Application.Abstractions.Security;

/// <summary>Configuration for issuing and validating API JWTs (bound from the <c>Jwt</c> config section).</summary>
public sealed class JwtOptions
{
    /// <summary>The token issuer (<c>iss</c>).</summary>
    public string Issuer { get; set; } = "One4All";

    /// <summary>The token audience (<c>aud</c>).</summary>
    public string Audience { get; set; } = "One4All.Api";

    /// <summary>The symmetric signing key (HMAC-SHA256); must be at least 32 bytes.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>The access-token lifetime in minutes.</summary>
    public int ExpiryMinutes { get; set; } = 60;
}

/// <summary>An issued access token.</summary>
/// <param name="AccessToken">The signed JWT.</param>
/// <param name="ExpiresAtUtc">When the token expires (UTC).</param>
/// <param name="TokenType">The token scheme (always <c>Bearer</c>).</param>
public readonly record struct TokenResult(string AccessToken, DateTime ExpiresAtUtc, string TokenType = "Bearer");

/// <summary>
/// Issues signed JWT access tokens for authenticated API callers. Tokens carry identity claims only (subject,
/// user name, display name, admin flag); the server still resolves permissions from the database per request,
/// so a token never grants more than the user currently has.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>Issues an access token for a successful authentication.</summary>
    /// <param name="authentication">The successful authentication result.</param>
    /// <returns>The signed token and its expiry.</returns>
    TokenResult Issue(AuthenticationResult authentication);
}
