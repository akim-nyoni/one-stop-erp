using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using One4All.Application.Abstractions.Security;
using One4All.Core.Security;
using One4All.Infrastructure.Security;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the API JWT path's token service: issue a signed token and validate it as the middleware would.</summary>
public sealed class JwtTokenServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "One4All",
        Audience = "One4All.Api",
        SigningKey = "unit-test-signing-key-that-is-long-enough-32+",
        ExpiryMinutes = 30,
    };

    [Fact]
    public void Issued_token_validates_and_carries_the_identity_claims()
    {
        JwtTokenService service = new(Options);
        AuthenticationResult auth = AuthenticationResult.Success(
            userId: 42, userName: "jbloggs", displayName: "Jo Bloggs",
            isSystemAdministrator: true, mustChangePassword: false, permissions: EffectivePermissions.None);

        TokenResult token = service.Issue(auth);

        token.TokenType.Should().Be("Bearer");
        token.ExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(30), TimeSpan.FromMinutes(1));

        TokenValidationParameters parameters = new()
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = Options.Issuer,
            ValidAudience = Options.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Options.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Name,
        };

        JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };
        ClaimsPrincipal principal = handler.ValidateToken(token.AccessToken, parameters, out _);

        principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value.Should().Be("42");
        principal.FindFirst(JwtRegisteredClaimNames.UniqueName)?.Value.Should().Be("jbloggs");
        principal.Identity!.Name.Should().Be("Jo Bloggs");
        principal.FindFirst(JwtTokenService.AdminClaim)?.Value.Should().Be("true");
    }

    [Fact]
    public void A_short_signing_key_is_rejected()
    {
        Action act = () => _ = new JwtTokenService(new JwtOptions { SigningKey = "too-short" });
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_token_signed_with_a_different_key_fails_validation()
    {
        JwtTokenService service = new(Options);
        TokenResult token = service.Issue(AuthenticationResult.Success(1, "u", "U", false, false, EffectivePermissions.None));

        TokenValidationParameters wrongKey = new()
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-completely-different-signing-key-32bytes!")),
        };

        Action act = () => new JwtSecurityTokenHandler().ValidateToken(token.AccessToken, wrongKey, out _);
        act.Should().Throw<SecurityTokenException>();
    }
}
