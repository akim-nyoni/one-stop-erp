using System.Text.RegularExpressions;
using One4All.Core.Security;

namespace One4All.Application.Security;

/// <summary>What an API request needs: the permission modules it falls under and whether it changes data.</summary>
/// <param name="Modules">Permission modules (any one suffices); empty for requests any signed-in user may make.</param>
/// <param name="IsWrite">Whether it changes data.</param>
public sealed record ApiRequirement(IReadOnlyList<string> Modules, bool IsWrite);

/// <summary>
/// Maps API routes to the permissions they need (brief §5.1): each area of the API belongs to one or more permission modules; reading
/// needs any permission in the module, changing data needs one that can create, edit, post, approve, delete or reverse. Pure.
/// </summary>
public static partial class ApiAccessRules
{
    /// <summary>The operations any of which lets a user change data in a module.</summary>
    public const OperationFlags WriteOperations = OperationFlags.Create | OperationFlags.Edit | OperationFlags.Delete | OperationFlags.Post | OperationFlags.Approve | OperationFlags.VoidReverse;

    /// <summary>
    /// Routes called without a signed-in user, each protecting itself: sign-in (credentials) and the WhatsApp webhook (Meta's verify token and
    /// signed deliveries). Any other route must belong to a governed area.
    /// </summary>
    public static readonly IReadOnlyList<string> AnonymousRoutes = ["/api/auth/login", "/api/webhooks/whatsapp", "/api/workflow/email/{token}"];

    /// <summary>Query parameters naming the user acting: they must be the signed-in user.</summary>
    public static readonly IReadOnlyList<string> ActorParameters = ["userId", "approverUserId"];

    /// <summary>
    /// Body fields that record who acted or who gave an authority: over the API they must be the signed-in user - a client cannot act for, or
    /// claim the authority of, anyone else (a supervisor authorises by making the request themselves).
    /// </summary>
    public static readonly IReadOnlySet<string> BodyActorFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "userId", "approverUserId", "requestedByUserId", "cashierUserId", "preparedByUserId", "amendedByUserId", "uploadedByUserId", "savedByUserId",
        "placedByUserId", "liftedByUserId", "initiatedByUserId", "commentedByUserId", "submittedByUserId", "receivedByUserId", "raisedByUserId",
        "postedByUserId", "enteredByUserId", "countedByUserId", "clearedByUserId", "approvedByUserId", "budgetApprovedByUserId",
        "authorisedByUserId", "rateOverrideByUserId", "overReceiptAuthorisedByUserId", "negativeAuthorisedByUserId", "creditLimitOverrideByUserId",
        "budgetOverrideAuthorisedByUserId",
    };

    // Each area belongs to the modules that declare it (brief §10: modules register their API areas).
    private static readonly Dictionary<string, string[]> ModulesByArea = Modules.BuiltInModules.All
        .SelectMany(m => m.ApiAreas.Select(area => (Area: area, Module: m.Code)))
        .GroupBy(x => x.Area, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Select(x => x.Module).ToArray(), StringComparer.OrdinalIgnoreCase);

    // Areas a signed-in user may use for themselves: the acting user is enforced instead.
    private static readonly HashSet<string> PersonalAreas = new(StringComparer.OrdinalIgnoreCase) { "me", "ess", "workflow", "notifications", "auth" };

    /// <summary>What a request needs, or null when the route is not governed (unknown areas are refused by the caller).</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The request path.</param>
    /// <returns>The requirement, or null for an unknown area.</returns>
    public static ApiRequirement? Requirement(string method, string path)
    {
        string[] segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        bool write = !HttpMethods.Contains(method) && !ReadOnlyPost().IsMatch(path);
        if (PersonalAreas.Contains(segments[1]))
        {
            return new ApiRequirement([], write);
        }

        // Master data is governed per record kind: customers by the sales ledger, suppliers by the purchase ledger, items by inventory.
        string area = string.Equals(segments[1], "master", StringComparison.OrdinalIgnoreCase) && segments.Length > 2 ? $"master/{segments[2]}" : segments[1];
        return ModulesByArea.TryGetValue(area, out string[]? modules) ? new ApiRequirement(modules, write) : null;
    }

    /// <summary>The licence modules behind an API area's permission modules (from the permission catalogue); any one licensed suffices.</summary>
    /// <param name="requirement">The requirement.</param>
    /// <returns>The licence module codes (empty for personal areas).</returns>
    public static IReadOnlyList<string> LicenceModules(ApiRequirement requirement)
        => [.. PermissionCatalogue.All.Where(p => requirement.Modules.Contains(p.Module, StringComparer.OrdinalIgnoreCase)).Select(p => p.LicenceModule).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Whether a route takes its acting-user parameter as the target of the action rather than the actor.</summary>
    /// <param name="path">The request path.</param>
    /// <returns>True when the user id names someone else (e.g. the assignee of a fault).</returns>
    public static bool UserIdIsTarget(string path) => TargetUser().IsMatch(path);

    /// <summary>Whether the permissions allow the requirement.</summary>
    /// <param name="permissions">The user's permissions.</param>
    /// <param name="requirement">What the request needs.</param>
    /// <returns>True when allowed.</returns>
    public static bool Allows(EffectivePermissions permissions, ApiRequirement requirement)
    {
        if (permissions.IsSuperAdministrator || requirement.Modules.Count == 0)
        {
            return true;
        }

        return requirement.Modules.Any(module => permissions.Codes.Any(code => code.StartsWith(module + ".", StringComparison.OrdinalIgnoreCase)
            && (requirement.IsWrite ? (permissions.OperationsFor(code) & WriteOperations) != OperationFlags.None : permissions.OperationsFor(code) != OperationFlags.None)));
    }

    private static readonly HashSet<string> HttpMethods = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS" };

    // POSTs that only read: running or previewing a report or template.
    [GeneratedRegex(@"^/api/(reports/[^/]+/run|reports/subscriptions/[^/]+/run|messages/templates/[^/]+/preview)$", RegexOptions.IgnoreCase)]
    private static partial Regex ReadOnlyPost();

    [GeneratedRegex(@"^/api/municipal/faults/[^/]+/assign$", RegexOptions.IgnoreCase)]
    private static partial Regex TargetUser();
}
