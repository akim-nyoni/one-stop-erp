using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using One4All.Application.Abstractions.Security;

namespace One4All.Infrastructure.Security;

/// <summary>
/// Issues HMAC-SHA256 signed JWT access tokens from the configured <see cref="JwtOptions"/>. Tokens carry the
/// caller's identity only; API endpoints resolve permissions from the database per request.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    /// <summary>The claim type carrying the admin flag.</summary>
    public const string AdminClaim = "one4all:admin";

    /// <summary>The claim carrying the sign-in session the token belongs to (ending the session revokes the token).</summary>
    public const string SessionClaim = "sid";

    /// <summary>The claim marking a token whose user must change their password before doing anything else.</summary>
    public const string PasswordChangeClaim = "one4all:pwd_change";

    private readonly JwtOptions _options;

    /// <summary>Creates the token service.</summary>
    /// <param name="options">The JWT options (issuer, audience, signing key, expiry).</param>
    public JwtTokenService(JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SigningKey) || Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
        {
            throw new InvalidOperationException("The JWT signing key must be configured and at least 32 bytes long.");
        }

        _options = options;
    }

    /// <inheritdoc />
    public TokenResult Issue(AuthenticationResult authentication)
    {
        if (!authentication.Succeeded)
        {
            throw new InvalidOperationException("Cannot issue a token for a failed authentication.");
        }

        DateTime expires = DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes);

        List<Claim> claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, authentication.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(JwtRegisteredClaimNames.UniqueName, authentication.UserName),
            new Claim(JwtRegisteredClaimNames.Name, authentication.DisplayName),
            new Claim(AdminClaim, authentication.IsSystemAdministrator ? "true" : "false"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        ];
        if (authentication.SessionId is string session)
        {
            claims.Add(new Claim(SessionClaim, session));
        }

        if (authentication.MustChangePassword)
        {
            claims.Add(new Claim(PasswordChangeClaim, "true"));
        }

        SymmetricSecurityKey key = new(Encoding.UTF8.GetBytes(_options.SigningKey));
        SigningCredentials credentials = new(key, SecurityAlgorithms.HmacSha256);

        JwtSecurityToken token = new(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: credentials);

        string encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return new TokenResult(encoded, expires);
    }
}
