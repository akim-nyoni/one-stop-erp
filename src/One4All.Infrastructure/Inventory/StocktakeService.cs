using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>
/// Runs physical stocktakes: freezes the warehouse's on-hand quantities into a session, captures counts,
/// and on posting adjusts each counted line's variance into stock through the inventory service (so the
/// moving-average valuation and the GL stay consistent with a single-item adjustment).
/// </summary>
public sealed class StocktakeService : IStocktakeService
{
    private readonly One4AllDbContext _db;
    private readonly IInventoryService _inventory;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="inventory">The inventory service that posts the variance adjustments.</param>
    public StocktakeService(One4AllDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    /// <inheritdoc />
    public async Task<Result<long>> StartAsync(long companyId, long warehouseId, DateOnly date, string reference, CancellationToken ct = default, decimal? recountTolerancePercent = null)
    {
        if (string.IsNullOrWhiteSpace(reference) || recountTolerancePercent < 0m)
        {
            return Error.Validation("Stocktake.Reference", "A stocktake reference is required, and any recount tolerance cannot be negative.");
        }

        List<StockBalance> balances = await _db.Set<StockBalance>()
            .Where(b => b.CompanyId == companyId && b.WarehouseId == warehouseId)
            .OrderBy(b => b.ItemId)
            .ToListAsync(ct).ConfigureAwait(false);

        StocktakeSession session = new(companyId, warehouseId, reference.Trim(), date) { RecountTolerancePercent = recountTolerancePercent };
        foreach (StockBalance balance in balances)
        {
            session.AddLine(balance.ItemId, balance.QuantityOnHand, balance.AverageCost);
        }

        _db.Set<StocktakeSession>().Add(session);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return session.Id;
    }

    /// <inheritdoc />
    public async Task<Result> RecordCountAsync(long companyId, long stocktakeId, long itemId, decimal countedQuantity, CancellationToken ct = default, long? countedByUserId = null)
    {
        if (countedQuantity < 0m)
        {
            return Result.Failure(Error.Validation("Stocktake.Count", "A counted quantity cannot be negative."));
        }

        StocktakeSession? session = await LoadDraftAsync(companyId, stocktakeId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Result.Failure(Error.NotFound("Stocktake.NotFound", $"No draft stocktake {stocktakeId}."));
        }

        StocktakeLine? line = session.Lines.FirstOrDefault(l => l.ItemId == itemId);
        if (line is null)
        {
            return Result.Failure(Error.NotFound("Stocktake.ItemNotOnSheet", $"Item {itemId} is not on stocktake {stocktakeId}."));
        }

        if (line.IsRecounted)
        {
            return Result.Failure(Error.Rule("Stocktake.AlreadyRecounted", "The item was recounted; its count stands."));
        }

        session.RecordCount(itemId, countedQuantity);
        line.CountedByUserId = countedByUserId;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> RecordRecountAsync(long companyId, long stocktakeId, long itemId, decimal quantity, long userId, CancellationToken ct = default)
    {
        StocktakeSession? session = await LoadDraftAsync(companyId, stocktakeId, ct).ConfigureAwait(false);
        StocktakeLine? line = session?.Lines.FirstOrDefault(l => l.ItemId == itemId);
        if (session is null || line is null)
        {
            return Result.Failure(Error.NotFound("Stocktake.NotFound", $"Item {itemId} is not on draft stocktake {stocktakeId}."));
        }

        if (quantity < 0m || !session.NeedsRecount(line))
        {
            return Result.Failure(Error.Rule("Stocktake.NoRecountDue", "Only a count outside the tolerance is recounted, once, and never to a negative quantity."));
        }

        if (line.CountedByUserId == userId)
        {
            return Result.Failure(Error.Rule("Stocktake.SameCounter", "A recount must be made by someone other than the first counter."));
        }

        line.Recount(quantity, userId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<CountSheetLine>>> CountSheetAsync(long companyId, long stocktakeId, CancellationToken ct = default)
    {
        StocktakeSession? session = await _db.Set<StocktakeSession>().AsNoTracking().Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == stocktakeId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Error.NotFound("Stocktake.NotFound", $"No stocktake {stocktakeId}.");
        }

        List<long> ids = [.. session.Lines.Select(l => l.ItemId)];
        Dictionary<long, (string Code, string Description)> items = await _db.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => (i.Code, i.Description), ct).ConfigureAwait(false);
        List<CountSheetLine> sheet = [.. session.Lines
            .Select(l => new CountSheetLine(l.ItemId, items.GetValueOrDefault(l.ItemId).Code ?? string.Empty, items.GetValueOrDefault(l.ItemId).Description ?? string.Empty, l.IsCounted, session.NeedsRecount(l), l.IsRecounted))
            .OrderBy(l => l.ItemCode, StringComparer.Ordinal)];
        return sheet;
    }

    /// <inheritdoc />
    public Task<Result<StocktakePostResult>> PostAsync(long companyId, long stocktakeId, DateOnly date, string adjustmentAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => PostCoreAsync(companyId, stocktakeId, date, adjustmentAccountCode, ct), ct);

    // One unit of work: every variance adjustment and the session's close commit together.
    private async Task<Result<StocktakePostResult>> PostCoreAsync(long companyId, long stocktakeId, DateOnly date, string adjustmentAccountCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(adjustmentAccountCode))
        {
            return Error.Validation("Stocktake.Account", "The stock-adjustment account is required.");
        }

        StocktakeSession? session = await LoadDraftAsync(companyId, stocktakeId, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Error.NotFound("Stocktake.NotFound", $"No draft stocktake {stocktakeId}.");
        }

        int due = session.Lines.Count(session.NeedsRecount);
        if (due > 0)
        {
            return Error.Rule("Stocktake.RecountsDue", $"{due} count(s) are outside the tolerance and must be recounted before posting.");
        }

        int posted = 0;
        decimal totalVariance = 0m;
        foreach (StocktakeLine line in session.Lines.Where(l => l.IsCounted))
        {
            Result<decimal> adjustment = await _inventory.AdjustAsync(
                companyId, line.ItemId, session.WarehouseId, date, line.CountedQuantity!.Value, adjustmentAccountCode, session.Reference, ct: ct)
                .ConfigureAwait(false);
            if (adjustment.IsFailure)
            {
                return adjustment.Error;
            }

            if (adjustment.Value != 0m)
            {
                posted++;
                totalVariance += adjustment.Value;
            }
        }

        session.MarkPosted();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new StocktakePostResult(posted, decimal.Round(totalVariance, 2, MidpointRounding.ToEven));
    }

    private async Task<StocktakeSession?> LoadDraftAsync(long companyId, long stocktakeId, CancellationToken ct)
    {
        StocktakeSession? session = await _db.Set<StocktakeSession>()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == stocktakeId && s.CompanyId == companyId, ct)
            .ConfigureAwait(false);

        return session is { Status: StocktakeStatus.Draft } ? session : null;
    }
}
