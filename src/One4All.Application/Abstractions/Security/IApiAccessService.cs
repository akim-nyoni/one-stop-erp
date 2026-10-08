namespace One4All.Application.Abstractions.Security;

/// <summary>An API request to authorise.</summary>
/// <param name="UserId">The signed-in user.</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The path.</param>
/// <param name="Query">The query parameters (name → value).</param>
/// <param name="Body">The top-level scalar values of a JSON request body (name → value), checked like the query: a body cannot name a company,
/// branch or acting user the query could not.</param>
public sealed record ApiAccessRequest(long UserId, string Method, string Path, IReadOnlyDictionary<string, string> Query, IReadOnlyDictionary<string, string>? Body = null);

/// <summary>The decision.</summary>
/// <param name="Allowed">Whether the request may proceed.</param>
/// <param name="StatusCode">The HTTP status when refused (403, or 404 for an unknown API area).</param>
/// <param name="Reason">Why it was refused.</param>
/// <param name="MissingActorParameters">Acting-user parameters the request left out, to fill in with the signed-in user.</param>
public sealed record ApiAccessDecision(bool Allowed, int StatusCode, string? Reason, IReadOnlyList<string> MissingActorParameters)
{
    /// <summary>A refusal.</summary>
    /// <param name="status">The status.</param>
    /// <param name="reason">Why.</param>
    /// <returns>The decision.</returns>
    public static ApiAccessDecision Refuse(int status, string reason) => new(false, status, reason, []);
}

/// <summary>
/// Authorises API requests (brief §5.1): the route's permission module and operation, the company asked for against the user's company
/// scopes, and that whoever the request says is acting is the signed-in user.
/// </summary>
public interface IApiAccessService
{
    /// <summary>Decides whether a request may proceed.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The decision.</returns>
    Task<ApiAccessDecision> AuthoriseAsync(ApiAccessRequest request, CancellationToken ct = default);
}
