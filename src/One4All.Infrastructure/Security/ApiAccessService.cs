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
    public ApiAccessService(One4AllDbContext db, IPermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    /// <inheritdoc />
    public async Task<ApiAccessDecision> AuthoriseAsync(ApiAccessRequest request, CancellationToken ct = default)
    {
        ApiRequirement? requirement = ApiAccessRules.Requirement(request.Method, request.Path);
        if (requirement is null)
        {
            return ApiAccessDecision.Refuse(404, "Unknown API area.");
        }

        EffectivePermissions permissions = await _permissions.ResolveAsync(request.UserId, ct).ConfigureAwait(false);
        if (!ApiAccessRules.Allows(permissions, requirement))
        {
            return ApiAccessDecision.Refuse(403, $"You have no permission to {(requirement.IsWrite ? "change" : "view")} {string.Join(" or ", requirement.Modules)}.");
        }

        // Whoever the request says is acting must be the signed-in user; left out, it is filled in.
        List<string> missing = [];
        if (!ApiAccessRules.UserIdIsTarget(request.Path))
        {
            foreach (string name in ApiAccessRules.ActorParameters)
            {
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

        string? company = request.Query.FirstOrDefault(q => string.Equals(q.Key, "companyId", StringComparison.OrdinalIgnoreCase)).Value;
        if (company is not null && !permissions.IsSuperAdministrator)
        {
            if (!long.TryParse(company, NumberStyles.Integer, CultureInfo.InvariantCulture, out long companyId) || !await CompanyAllowedAsync(request.UserId, companyId, ct).ConfigureAwait(false))
            {
                return ApiAccessDecision.Refuse(403, "You have no access to that company.");
            }
        }

        return new ApiAccessDecision(true, 200, null, missing);
    }

    // A user is limited to the companies their role assignments are scoped to; any assignment without a company scope opens them all.
    private async Task<bool> CompanyAllowedAsync(long userId, long companyId, CancellationToken ct)
    {
        List<UserRole> assignments = await _db.Set<UserRole>().AsNoTracking().Include(r => r.Scopes).Where(r => r.UserId == userId).ToListAsync(ct).ConfigureAwait(false);
        if (assignments.Count == 0 || assignments.Any(a => a.Scopes.All(s => s.Dimension != ScopeDimension.Company)))
        {
            return true;
        }

        string? code = await _db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        string id = companyId.ToString(CultureInfo.InvariantCulture);
        return assignments.SelectMany(a => a.Scopes).Where(s => s.Dimension == ScopeDimension.Company).Any(s => s.Allows(id) || (code is not null && s.Allows(code)));
    }
}
