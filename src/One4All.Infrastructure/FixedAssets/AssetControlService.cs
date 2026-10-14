using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.FixedAssets;
using One4All.Application.Abstractions.Numbering;
using One4All.Core.Common;
using One4All.Core.FixedAssets;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.FixedAssets;

/// <summary>Runs asset verifications and planned maintenance.</summary>
public sealed class AssetControlService : IAssetControlService
{
    private readonly One4AllDbContext _db;
    private readonly IFixedAssetService _assets;
    private readonly INumberSequenceService _numbers;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="assets">Moves assets and logs maintenance.</param>
    /// <param name="numbers">Numbers the counts.</param>
    public AssetControlService(One4AllDbContext db, IFixedAssetService assets, INumberSequenceService numbers)
    {
        _db = db;
        _assets = assets;
        _numbers = numbers;
    }

    /// <inheritdoc />
    public Task<Result<long>> StartCountAsync(long companyId, DateOnly date, string? location, CancellationToken ct = default)
        => _db.AtomicAsync(() => StartCountCoreAsync(companyId, date, string.IsNullOrWhiteSpace(location) ? null : location.Trim(), ct), ct);

    private async Task<Result<long>> StartCountCoreAsync(long companyId, DateOnly date, string? location, CancellationToken ct)
    {
        List<FixedAsset> expected = await _db.Set<FixedAsset>().AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.Status != FixedAssetStatus.Disposed && a.AcquisitionDate <= date && (location == null || a.Location == location))
            .OrderBy(a => a.AssetNumber).ToListAsync(ct).ConfigureAwait(false);
        Result<string> number = await _numbers.AllocateAsync(companyId, "FAC", date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        AssetCount count = new(companyId, number.Value, date, location);
        expected.ForEach(a => count.AddLine(new AssetCountLine(a.Id, a.AssetNumber, a.Location, AssetCountResult.NotSeen)));
        _db.AssetCounts.Add(count);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return count.Id;
    }

    /// <inheritdoc />
    public async Task<Result> RecordSightingAsync(long companyId, long countId, string tag, string? foundLocation, string? condition, CancellationToken ct = default)
    {
        AssetCount? count = await _db.AssetCounts.Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == countId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (count is null)
        {
            return Result.Failure(Error.NotFound("Fa.CountNotFound", $"Asset count {countId} not found."));
        }

        if (count.IsClosed)
        {
            return Result.Failure(Error.Rule("Fa.CountClosed", $"Count {count.CountNumber} is closed."));
        }

        string scanned = tag.Trim().ToUpperInvariant();
        if (scanned.Length == 0)
        {
            return Result.Failure(Error.Validation("Fa.TagRequired", "A sighting needs the asset number or tag."));
        }

        AssetCountLine? line = count.Lines.FirstOrDefault(l => string.Equals(l.Tag, scanned, StringComparison.OrdinalIgnoreCase));
        if (line is null)
        {
            // The scan is the asset number or the barcode / tag fixed to it.
            FixedAsset? asset = await _db.Set<FixedAsset>().AsNoTracking().FirstOrDefaultAsync(a => a.CompanyId == companyId && (a.AssetNumber == scanned || a.TagNumber == scanned), ct)
                .ConfigureAwait(false);
            line = asset is not null ? count.Lines.FirstOrDefault(l => l.AssetId == asset.Id) : null;
        }

        if (line is null)
        {
            FixedAsset? asset = await _db.Set<FixedAsset>().AsNoTracking().FirstOrDefaultAsync(a => a.CompanyId == companyId && (a.AssetNumber == scanned || a.TagNumber == scanned), ct)
                .ConfigureAwait(false);
            line = asset is null
                ? new AssetCountLine(null, scanned, null, AssetCountResult.Unregistered)
                : new AssetCountLine(asset.Id, asset.AssetNumber, asset.Location, AssetCountResult.NotSeen);
            count.AddLine(line);
        }

        line.Sight(foundLocation?.Trim(), condition?.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<int>> CloseCountAsync(long companyId, long countId, bool updateLocations, CancellationToken ct = default)
        => _db.AtomicAsync(() => CloseCountCoreAsync(companyId, countId, updateLocations, ct), ct);

    // One unit of work: the location moves and the closed count commit together.
    private async Task<Result<int>> CloseCountCoreAsync(long companyId, long countId, bool updateLocations, CancellationToken ct)
    {
        AssetCount? count = await _db.AssetCounts.Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == countId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (count is null)
        {
            return Error.NotFound("Fa.CountNotFound", $"Asset count {countId} not found.");
        }

        if (count.IsClosed)
        {
            return Error.Rule("Fa.CountClosed", $"Count {count.CountNumber} is already closed.");
        }

        if (updateLocations)
        {
            foreach (AssetCountLine moved in count.Lines.Where(l => l is { Result: AssetCountResult.FoundElsewhere, AssetId: not null }))
            {
                Result transfer = await _assets.TransferAsync(companyId, moved.AssetId!.Value, moved.FoundLocation, null, ct).ConfigureAwait(false);
                if (transfer.IsFailure)
                {
                    return transfer.Error;
                }
            }
        }

        count.Close();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return count.Lines.Count(l => l.Result == AssetCountResult.Missing);
    }

    /// <inheritdoc />
    public async Task<Result<long>> PlanMaintenanceAsync(long companyId, long assetId, string description, int intervalMonths, DateOnly firstDue, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description) || intervalMonths <= 0)
        {
            return Error.Validation("Fa.PlanInvalid", "A maintenance plan needs a task and a positive interval in months.");
        }

        if (!await _db.Set<FixedAsset>().AnyAsync(a => a.Id == assetId && a.CompanyId == companyId && a.Status != FixedAssetStatus.Disposed, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Fa.AssetNotFound", $"No held asset {assetId}.");
        }

        MaintenancePlan plan = new(companyId, assetId, description.Trim(), intervalMonths, firstDue);
        _db.MaintenancePlans.Add(plan);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return plan.Id;
    }

    /// <inheritdoc />
    public Task<Result<long>> CompletePlannedAsync(long companyId, long planId, DateOnly date, decimal cost, string? provider, CancellationToken ct = default)
        => _db.AtomicAsync(() => CompletePlannedCoreAsync(companyId, planId, date, cost, provider, ct), ct);

    // One unit of work: the history entry and the rolled plan commit together.
    private async Task<Result<long>> CompletePlannedCoreAsync(long companyId, long planId, DateOnly date, decimal cost, string? provider, CancellationToken ct)
    {
        MaintenancePlan? plan = await _db.MaintenancePlans.FirstOrDefaultAsync(p => p.Id == planId && p.CompanyId == companyId && p.IsActive, ct).ConfigureAwait(false);
        if (plan is null)
        {
            return Error.NotFound("Fa.PlanNotFound", $"No active maintenance plan {planId}.");
        }

        Result<long> logged = await _assets.RecordMaintenanceAsync(companyId, plan.AssetId, date, $"{plan.Description} (planned)", cost, provider, ct).ConfigureAwait(false);
        if (logged.IsFailure)
        {
            return logged.Error;
        }

        plan.Done(date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return logged.Value;
    }
}
