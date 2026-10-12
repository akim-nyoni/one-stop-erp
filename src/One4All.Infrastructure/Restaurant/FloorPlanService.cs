using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Common;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Restaurant;

/// <summary>Keeps the floor plan and flags tabs open too long or running too high.</summary>
public sealed class FloorPlanService : IFloorPlanService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public FloorPlanService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> PlaceTableAsync(long companyId, string tableNumber, int x, int y, TableShape shape, CancellationToken ct = default)
    {
        if (x is < 0 or > 99 || y is < 0 or > 99)
        {
            return Result.Failure(Error.Validation("Res.PlanPlace", "A table sits on the plan grid at 0–99 across and 0–99 down."));
        }

        string number = (tableNumber ?? string.Empty).Trim().ToUpperInvariant();
        List<RestaurantTable> tables = await _db.Set<RestaurantTable>().Where(t => t.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);
        if (tables.Find(t => string.Equals(t.TableNumber, number, StringComparison.OrdinalIgnoreCase)) is not RestaurantTable table)
        {
            return Result.Failure(Error.NotFound("Res.TableNotFound", $"Table {number} not found."));
        }

        if (tables.Find(t => t.Id != table.Id && t.PlanX == x && t.PlanY == y) is RestaurantTable other)
        {
            return Result.Failure(Error.Conflict("Res.PlanTaken", $"Table {other.TableNumber} already sits there."));
        }

        (table.PlanX, table.PlanY, table.Shape) = (x, y, shape);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetTabWarningsAsync(long companyId, int minutes, decimal amount, CancellationToken ct = default)
    {
        if (minutes < 0 || amount < 0m)
        {
            return Result.Failure(Error.Validation("Res.TabWarnings", "Tab warnings are a number of minutes and a total, 0 for none."));
        }

        RestaurantPolicy? policy = await _db.Set<RestaurantPolicy>().FirstOrDefaultAsync(p => p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (policy is null)
        {
            policy = new RestaurantPolicy(companyId, 0, 0m, string.Empty);
            _db.Set<RestaurantPolicy>().Add(policy);
        }

        (policy.TabWarningMinutes, policy.TabWarningAmount) = (minutes, amount);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FloorTable>> PlanAsync(long companyId, DateTimeOffset now, CancellationToken ct = default)
    {
        List<RestaurantTable> tables = await _db.Set<RestaurantTable>().AsNoTracking().Where(t => t.CompanyId == companyId).OrderBy(t => t.TableNumber).ToListAsync(ct).ConfigureAwait(false);
        List<long> ids = [.. tables.Select(t => t.Id)];
        List<TableOrder> open = await _db.Set<TableOrder>().AsNoTracking().Include(o => o.Lines)
            .Where(o => o.CompanyId == companyId && o.Status == TableOrderStatus.Open && o.TableId != null && ids.Contains(o.TableId.Value)).ToListAsync(ct).ConfigureAwait(false);
        var policy = await _db.Set<RestaurantPolicy>().AsNoTracking().Where(p => p.CompanyId == companyId).Select(p => new { p.TabWarningMinutes, p.TabWarningAmount })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        List<FloorTable> plan = [];
        foreach (RestaurantTable table in tables)
        {
            TableOrder? order = open.Where(o => o.TableId == table.Id).OrderBy(o => o.CreatedOnUtc).FirstOrDefault();
            int minutes = order is null ? 0 : Math.Max(0, (int)(now - order.CreatedOnUtc).TotalMinutes);
            decimal total = order?.GrossTotal ?? 0m;
            List<string> flags = [];
            if (order is not null && policy is { TabWarningMinutes: > 0 } && minutes >= policy.TabWarningMinutes)
            {
                flags.Add($"open {minutes} min");
            }

            if (order is not null && policy is { TabWarningAmount: > 0m } && total > policy.TabWarningAmount)
            {
                flags.Add($"over {policy.TabWarningAmount.ToString("N2", CultureInfo.InvariantCulture)}");
            }

            plan.Add(new FloorTable(table.TableNumber, table.Seats, table.PlanX, table.PlanY, table.Shape, table.Status, order?.OrderNumber, decimal.Round(total, 2), minutes,
                flags.Count == 0 ? null : string.Join("; ", flags)));
        }

        return plan;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FloorTable>> WarningsAsync(long companyId, DateTimeOffset now, CancellationToken ct = default)
        => [.. (await PlanAsync(companyId, now, ct).ConfigureAwait(false)).Where(t => t.Warning is not null).OrderByDescending(t => t.MinutesOpen)];
}
