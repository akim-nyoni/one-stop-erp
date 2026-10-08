using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using One4All.Application.Abstractions.Security;

namespace One4All.Server;

/// <summary>
/// Signs in an integration by its <c>X-Api-Key</c> header (brief §10): the principal is the service user the client acts as, so the same permission,
/// company and scope checks apply as for that user; the client id rides along for the rate limiter and the audit trail.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>The scheme name.</summary>
    public const string Scheme = "ApiKey";

    /// <summary>The header carrying the key.</summary>
    public const string Header = "X-Api-Key";

    /// <summary>The claim naming the client.</summary>
    public const string ClientClaim = "client_id";

    /// <summary>The claim carrying the client's own rate limit.</summary>
    public const string RateClaim = "client_rpm";

    /// <summary>Creates the handler.</summary>
    /// <param name="options">The scheme options.</param>
    /// <param name="logger">The logger factory.</param>
    /// <param name="encoder">The URL encoder.</param>
    public ApiKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? key = Request.Headers[Header].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key))
        {
            return AuthenticateResult.NoResult();
        }

        One4All.Core.Common.Result<ApiClientIdentity> client = await Context.RequestServices.GetRequiredService<IApiClientService>().AuthenticateAsync(key, Context.RequestAborted).ConfigureAwait(false);
        if (client.IsFailure)
        {
            return AuthenticateResult.Fail(client.Error.Message);
        }

        List<Claim> claims =
        [
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, client.Value.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.UniqueName, $"{client.Value.UserName} (API {client.Value.ClientId})"),
            new(ClientClaim, client.Value.ClientId),
        ];
        if (client.Value.RequestsPerMinute is int rpm)
        {
            claims.Add(new Claim(RateClaim, rpm.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, Scheme, System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.UniqueName, null));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme));
    }
}
