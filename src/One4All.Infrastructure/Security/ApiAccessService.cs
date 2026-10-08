using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Security;
using One4All.Application.Security;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Security;

/// <summary>Authorises API requests by permission module, company scope and acting user (brief §5.1).</summary>
public sealed class ApiAccessService : IApiAccessService
{
    private readonly One4AllDbContext _db;
    private readonly IPermissionService _permissions;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="permissions">Resolves the user's permissions.</param>
    /// <param name="licence">Refuses unlicensed modules and changes while read-only.</param>
    /// <param name="scopes">Applies row-level data scopes.</param>
    public ApiAccessService(One4AllDbContext db, IPermissionService permissions, Application.Abstractions.Licensing.ILicenceService? licence = null, IDataScopeService? scopes = null)
    {
        _scopes = scopes;
        _db = db;
        _permissions = permissions;
        _licence = licence;
    }

    private readonly Application.Abstractions.Licensing.ILicenceService? _licence;
    private readonly IDataScopeService? _scopes;

    // Request parameters naming scoped data: by id (resolved to its code) or by code.
    private static readonly (string Name, ScopeDimension Dimension, bool IsId)[] ScopedParameters =
    [
        ("companyId", ScopeDimension.Company, true), ("branchId", ScopeDimension.Branch, true), ("warehouseId", ScopeDimension.Warehouse, true),
        ("terminalId", ScopeDimension.Till, true), ("contractId", ScopeDimension.Job, true), ("jobId", ScopeDimension.Job, true),
        ("costCentre", ScopeDimension.CostCentre, false), ("department", ScopeDimension.Department, false), ("ward", ScopeDimension.WardZone, false),
    ];

    /// <inheritdoc />
    public async Task<ApiAccessDecision> AuthoriseAsync(ApiAccessRequest request, CancellationToken ct = default)
    {
        ApiRequirement? requirement = ApiAccessRules.Requirement(request.Method, request.Path);
        if (requirement is null)
        {
            return ApiAccessDecision.Refuse(404, "Unknown API area.");
        }

        // The licence: an area needs one of its modules licensed, and nothing changes while it is read-only (brief §11.5).
        if (_licence is { Enforced: true })
        {
            IReadOnlyList<string> licences = ApiAccessRules.LicenceModules(requirement);
            if (licences.Count > 0 && !licences.Any(_licence.IsModuleLicensed))
            {
                return ApiAccessDecision.Refuse(403, $"Not licensed: {string.Join(" / ", licences)}.");
            }

            if (requirement.IsWrite && _licence.CanPost() is { IsFailure: true } readOnly)
            {
                return ApiAccessDecision.Refuse(403, readOnly.Error.Message);
            }
        }

        EffectivePermissions permissions = await _permissions.ResolveAsync(request.UserId, ct).ConfigureAwait(false);
        if (!ApiAccessRules.Allows(permissions, requirement))
        {
            return ApiAccessDecision.Refuse(403, $"You have no permission to {(requirement.IsWrite ? "change" : "view")} {string.Join(" or ", requirement.Modules)}.");
        }

        // A JSON body is held to the same checks as the query: its company, branch and acting-user fields join the query's, and may not
        // contradict them.
        Dictionary<string, string> values = new(request.Query, StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in request.Body ?? new Dictionary<string, string>())
        {
            bool checkedField = ScopedParameters.Any(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)) || ApiAccessRules.BodyActorFields.Contains(key);
            if (!checkedField)
            {
                continue;
            }

            if (values.TryGetValue(key, out string? fromQuery) && !string.Equals(fromQuery, value, StringComparison.OrdinalIgnoreCase))
            {
                return ApiAccessDecision.Refuse(400, $"The request names two different {key} values.");
            }

            values[key] = value;
        }

        // Whoever the request says is acting (or authorising) must be the signed-in user; left out of the query, it is filled in.
        List<string> missing = [];
        if (!ApiAccessRules.UserIdIsTarget(request.Path))
        {
            foreach ((string key, string value) in request.Body ?? new Dictionary<string, string>())
            {
                if (ApiAccessRules.BodyActorFields.Contains(key) && value.Length > 0
                    && (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long bodyActor) || bodyActor != request.UserId))
                {
                    return ApiAccessDecision.Refuse(403, "Requests act, and authorise, as the signed-in user only.");
                }
            }

            foreach (string name in ApiAccessRules.ActorParameters)
            {
                if (request.Body?.Keys.Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) == true)
                {
                    continue;
                }

                string? given = request.Query.FirstOrDefault(q => string.Equals(q.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
                if (given is null)
                {
                    missing.Add(name);
                }
                else if (!long.TryParse(given, NumberStyles.Integer, CultureInfo.InvariantCulture, out long actor) || actor != request.UserId)
                {
                    return ApiAccessDecision.Refuse(403, "Requests act as the signed-in user only.");
                }
            }
        }

        // Row-level scopes: a company, branch, warehouse, till, job, cost centre, department or ward the request names must be within the
        // user's scopes (an assignment that leaves a dimension open opens it).
        if (!permissions.IsSuperAdministrator && _scopes is not null)
        {
            foreach ((string name, ScopeDimension dimension, bool isId) in ScopedParameters)
            {
                if (!values.TryGetValue(name, out string? value))
                {
                    continue;
                }

                bool allowed = isId
                    ? long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) && await _scopes.AllowsAsync(request.UserId, dimension, id, ct).ConfigureAwait(false)
                    : (await _scopes.ForUserAsync(request.UserId, ct).ConfigureAwait(false)).Allows(dimension, value);
                if (!allowed)
                {
                    return ApiAccessDecision.Refuse(403, $"You have no access to that {dimension}.");
                }
            }
        }

        return new ApiAccessDecision(true, 200, null, missing);
    }
}
