using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Security;
using One4All.Application.Security;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Security;

/// <summary>
/// Resolves row-level data scopes from a user's role assignments (brief §5.1). Roles that come through a group carry no scopes of their own:
/// they inherit the user's direct scopes, so a user whose direct assignments are limited to a branch or warehouse stays limited when they
/// join a group, and only a user with no direct assignments at all is unrestricted by a group's roles.
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
        // A group's roles take the user's own scopes; only with no direct assignments does a group with roles open everything.
        List<long> groupIds = [.. user.Groups.Select(g => g.GroupId)];
        if (assignments.Count == 0 && groupIds.Count > 0 && await _db.Groups.AnyAsync(g => groupIds.Contains(g.Id) && g.Roles.Any(), ct).ConfigureAwait(false))
        {
            assignments.Add(new Dictionary<ScopeDimension, IReadOnlyList<string>>());
        }

        return DataScope.From(assignments, user.IsSystemAdministrator);
    }

    /// <inheritdoc />
    public async Task<RowScope> RowScopeAsync(long userId, CancellationToken ct = default)
    {
        DataScope scope = await ForUserAsync(userId, ct).ConfigureAwait(false);
        static (string[] Codes, long[] Ids) Keys(IReadOnlyList<string> values)
            => ([.. values.Select(v => v.ToUpperInvariant())], [.. values.Select(v => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) ? id : 0L).Where(id => id > 0L)]);
        static string[]? Codes(IReadOnlyList<string>? values) => values is null ? null : [.. values.Select(v => v.ToUpperInvariant())];

        long[]? branches = null, warehouses = null, tills = null, jobs = null;
        if (scope.AllowedValues(ScopeDimension.Branch) is { } b)
        {
            (string[] codes, long[] ids) = Keys(b);
            branches = [.. await _db.Branches.AsNoTracking().Where(x => codes.Contains(x.Code.ToUpper()) || ids.Contains(x.Id)).Select(x => x.Id).ToListAsync(ct).ConfigureAwait(false)];
        }

        if (scope.AllowedValues(ScopeDimension.Warehouse) is { } w)
        {
            (string[] codes, long[] ids) = Keys(w);
            warehouses = [.. await _db.Warehouses.AsNoTracking().Where(x => codes.Contains(x.Code.ToUpper()) || ids.Contains(x.Id)).Select(x => x.Id).ToListAsync(ct).ConfigureAwait(false)];
        }

        if (scope.AllowedValues(ScopeDimension.Till) is { } t)
        {
            (string[] codes, long[] ids) = Keys(t);
            tills = [.. await _db.PosTerminals.AsNoTracking().Where(x => codes.Contains(x.Code.ToUpper()) || ids.Contains(x.Id)).Select(x => x.Id).ToListAsync(ct).ConfigureAwait(false)];
        }

        if (scope.AllowedValues(ScopeDimension.Job) is { } j)
        {
            (string[] codes, long[] ids) = Keys(j);
            jobs = [.. await _db.JobContracts.AsNoTracking().Where(x => codes.Contains(x.ContractNumber.ToUpper()) || ids.Contains(x.Id)).Select(x => x.Id).ToListAsync(ct).ConfigureAwait(false)];
        }

        return new RowScope(branches, warehouses, tills, jobs, Codes(scope.AllowedValues(ScopeDimension.CostCentre)), Codes(scope.AllowedValues(ScopeDimension.Department)),
            Codes(scope.AllowedValues(ScopeDimension.WardZone)));
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
