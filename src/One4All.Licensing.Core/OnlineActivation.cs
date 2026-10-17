using System.Net.Http.Json;
using System.Text.Json;

namespace One4All.Licensing.Core;

/// <summary>
/// The online activation contract (brief §11: an optional online activation service for self-service activation and renewal - the interfaces now,
/// the vendor's service later). The site sends its activation request code; the service answers with a signed licence (and, when it has one, the
/// latest revocation list), which the site validates and installs exactly as it would a file from the vendor.
/// </summary>
public static class OnlineActivation
{
    /// <summary>The service path that activates a site.</summary>
    public const string ActivatePath = "api/v1/activate";

    /// <summary>The service path that renews a site's licence.</summary>
    public const string RenewPath = "api/v1/renew";
}

/// <summary>What a site sends to activate or renew.</summary>
/// <param name="RequestCode">The site's activation request code (fingerprint, customer, site, time).</param>
/// <param name="CustomerKey">The customer's self-service key, if the vendor issued one.</param>
/// <param name="CurrentLicenceId">The licence in use, when renewing.</param>
public sealed record OnlineActivationRequest(string RequestCode, string? CustomerKey, string? CurrentLicenceId);

/// <summary>The service's answer.</summary>
/// <param name="Granted">Whether a licence was issued.</param>
/// <param name="LicenceToken">The signed licence token, when granted.</param>
/// <param name="RevocationListToken">The latest signed revocation list, if the service sends one.</param>
/// <param name="Message">Why not, or a note for the user.</param>
public sealed record OnlineActivationResponse(bool Granted, string? LicenceToken, string? RevocationListToken, string? Message);

/// <summary>A site's client for the vendor's online activation service.</summary>
public interface IOnlineActivationClient
{
    /// <summary>Gets a value indicating whether an activation service is configured.</summary>
    bool IsConfigured { get; }

    /// <summary>Asks the service to activate this site.</summary>
    Task<OnlineActivationResponse> ActivateAsync(OnlineActivationRequest request, CancellationToken ct = default);

    /// <summary>Asks the service to renew this site's licence.</summary>
    Task<OnlineActivationResponse> RenewAsync(OnlineActivationRequest request, CancellationToken ct = default);
}

/// <summary>The vendor service's side of the contract: decides a request and issues the signed licence.</summary>
public interface IActivationAuthority
{
    /// <summary>Activates a site from its request code (checking the customer's entitlement and machine count).</summary>
    Task<OnlineActivationResponse> ActivateAsync(OnlineActivationRequest request, CancellationToken ct = default);

    /// <summary>Renews a site's licence when its subscription allows.</summary>
    Task<OnlineActivationResponse> RenewAsync(OnlineActivationRequest request, CancellationToken ct = default);
}

/// <summary>The client when no activation service is configured: every request is answered with how to activate offline instead.</summary>
public sealed class OfflineOnlyActivationClient : IOnlineActivationClient
{
    /// <inheritdoc />
    public bool IsConfigured => false;

    /// <inheritdoc />
    public Task<OnlineActivationResponse> ActivateAsync(OnlineActivationRequest request, CancellationToken ct = default) => Task.FromResult(NotConfigured());

    /// <inheritdoc />
    public Task<OnlineActivationResponse> RenewAsync(OnlineActivationRequest request, CancellationToken ct = default) => Task.FromResult(NotConfigured());

    private static OnlineActivationResponse NotConfigured()
        => new(false, null, null, "No online activation service is configured: send the activation request code to your vendor and install the licence file they return.");
}

/// <summary>Calls the vendor's activation service over HTTPS (JSON in, JSON out); network and service failures come back as a refusal with the reason.</summary>
public sealed class HttpOnlineActivationClient : IOnlineActivationClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    /// <summary>Creates the client.</summary>
    /// <param name="http">An HTTP client whose base address is the service (https).</param>
    public HttpOnlineActivationClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (http.BaseAddress is null || (http.BaseAddress.Scheme != Uri.UriSchemeHttps && !http.BaseAddress.IsLoopback))
        {
            throw new ArgumentException("The activation service must be an https address.", nameof(http));
        }

        _http = http;
    }

    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <inheritdoc />
    public Task<OnlineActivationResponse> ActivateAsync(OnlineActivationRequest request, CancellationToken ct = default) => SendAsync(OnlineActivation.ActivatePath, request, ct);

    /// <inheritdoc />
    public Task<OnlineActivationResponse> RenewAsync(OnlineActivationRequest request, CancellationToken ct = default) => SendAsync(OnlineActivation.RenewPath, request, ct);

    private async Task<OnlineActivationResponse> SendAsync(string path, OnlineActivationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            using HttpResponseMessage response = await _http.PostAsJsonAsync(path, request, Json, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new OnlineActivationResponse(false, null, null, $"The activation service answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            OnlineActivationResponse? answer = await response.Content.ReadFromJsonAsync<OnlineActivationResponse>(Json, ct).ConfigureAwait(false);
            return answer ?? new OnlineActivationResponse(false, null, null, "The activation service sent an empty answer.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            return new OnlineActivationResponse(false, null, null, $"The activation service could not be reached: {ex.Message}");
        }
    }
}
