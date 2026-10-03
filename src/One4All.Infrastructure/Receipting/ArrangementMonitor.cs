using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Trading;
using One4All.Core.Common;
using One4All.Core.Receipting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Receipting;

/// <summary>Payment arrangement schedules and default tracking over AR receipts.</summary>
public sealed class ArrangementMonitor : IArrangementMonitor
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the monitor.</summary>
    /// <param name="db">The database context.</param>
    public ArrangementMonitor(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Instalment>>> ScheduleAsync(long companyId, long arrangementId, CancellationToken ct = default)
    {
        PaymentArrangement? arrangement = await _db.Set<PaymentArrangement>().AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == arrangementId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (arrangement is null)
        {
            return Error.NotFound("Rcp.ArrangementNotFound", $"Arrangement {arrangementId} not found.");
        }

        return Result<IReadOnlyList<Instalment>>.Success(InstalmentSchedule.Build(arrangement.TotalAmount, arrangement.InstalmentAmount, arrangement.StartDate));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArrangementReview>> ReviewAsync(long companyId, DateOnly asAtDate, int graceDays, int missedToDefault, CancellationToken ct = default)
    {
        // One save at the end: every status change commits together.
        List<PaymentArrangement> active = await _db.Set<PaymentArrangement>()
            .Where(a => a.CompanyId == companyId && a.Status == ArrangementStatus.Active && a.StartDate <= asAtDate)
            .OrderBy(a => a.Id).ToListAsync(ct).ConfigureAwait(false);
        List<long> customerIds = [.. active.Select(a => a.CustomerId).Distinct()];
        Dictionary<long, string> codes = await _db.Customers.Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct).ConfigureAwait(false);
        var receipts = await _db.OpenItems.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.ItemType == OpenItemType.Receipt
                && customerIds.Contains(i.TraderId) && i.DocumentDate <= asAtDate)
            .Select(i => new { i.TraderId, i.DocumentDate, i.OriginalBase })
            .ToListAsync(ct).ConfigureAwait(false);

        List<ArrangementReview> reviews = [];
        foreach (PaymentArrangement arrangement in active)
        {
            // Receipts from the month the arrangement starts count towards it.
            DateOnly from = new(arrangement.StartDate.Year, arrangement.StartDate.Month, 1);
            decimal paid = receipts.Where(r => r.TraderId == arrangement.CustomerId && r.DocumentDate >= from).Sum(r => r.OriginalBase);
            ArrangementStanding standing = InstalmentSchedule.Assess(
                InstalmentSchedule.Build(arrangement.TotalAmount, arrangement.InstalmentAmount, arrangement.StartDate), paid, asAtDate, graceDays, missedToDefault);
            if (standing.Honoured)
            {
                arrangement.MarkHonoured();
            }
            else if (standing.Defaulted)
            {
                arrangement.MarkDefaulted();
            }

            reviews.Add(new ArrangementReview(arrangement.Id, codes.GetValueOrDefault(arrangement.CustomerId, string.Empty), standing, arrangement.Status));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return reviews;
    }
}
