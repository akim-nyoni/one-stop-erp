using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Security;
using One4All.Application.Security;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Security;

/// <summary>
/// Resolves row-level data scopes from a user's role assignments (brief §5.1). Roles that come through a group carry no scopes, so a user
/// who belongs to a group that has roles is unrestricted for those roles.
/// </summary>
public sealed class DataScopeService : IDataScopeService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public DataScopeService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<DataScope> ForUserAsync(long userId, CancellationToken ct = default)
    {
        User? user = await _db.Users.AsNoTracking().Include(u => u.Roles).ThenInclude(r => r.Scopes).Include(u => u.Groups).FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return DataScope.Everything; // no such user: authentication refuses them before scopes matter
        }

        List<IReadOnlyDictionary<ScopeDimension, IReadOnlyList<string>>> assignments =
            [.. user.Roles.Select(r => (IReadOnlyDictionary<ScopeDimension, IReadOnlyList<string>>)r.Scopes.GroupBy(s => s.Dimension)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.SelectMany(s => s.Values)]))];
        List<long> groupIds = [.. user.Groups.Select(g => g.GroupId)];
        if (groupIds.Count > 0 && await _db.Groups.AnyAsync(g => groupIds.Contains(g.Id) && g.Roles.Any(), ct).ConfigureAwait(false))
        {
            assignments.Add(new Dictionary<ScopeDimension, IReadOnlyList<string>>());
        }

        return DataScope.From(assignments, user.IsSystemAdministrator);
    }

    /// <inheritdoc />
    public async Task<bool> AllowsAsync(long userId, ScopeDimension dimension, long id, CancellationToken ct = default)
    {
        DataScope scope = await ForUserAsync(userId, ct).ConfigureAwait(false);
        if (!scope.Restricts(dimension))
        {
            return true;
        }

        string? code = dimension switch
        {
            ScopeDimension.Company => await _db.Companies.Where(x => x.Id == id).Select(x => x.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false),
            ScopeDimension.Branch => await _db.Branches.Where(x => x.Id == id).Select(x => x.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false),
            ScopeDimension.Warehouse => await _db.Warehouses.Where(x => x.Id == id).Select(x => x.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false),
            ScopeDimension.Till => await _db.PosTerminals.Where(x => x.Id == id).Select(x => x.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false),
            ScopeDimension.CostCentre => await _db.CostCentres.Where(x => x.Id == id).Select(x => x.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false),
            ScopeDimension.Department => await _db.Departments.Where(x => x.Id == id).Select(x => x.Code).FirstOrDefaultAsync(ct).ConfigureAwait(false),
            ScopeDimension.Job => await _db.JobContracts.Where(x => x.Id == id).Select(x => x.ContractNumber).FirstOrDefaultAsync(ct).ConfigureAwait(false),
            _ => null,
        };
        return scope.Allows(dimension, code, id.ToString(CultureInfo.InvariantCulture));
    }
}
