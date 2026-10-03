using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>The promise-to-pay diary over the AR ledger's receipts.</summary>
public sealed class PromiseToPayService : IPromiseToPayService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public PromiseToPayService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<long>> RecordAsync(long companyId, long customerId, DateOnly madeOn, DateOnly payBy, decimal amount, string? note, long recordedByUserId, CancellationToken ct = default)
    {
        if (amount <= 0m || payBy < madeOn)
        {
            return Error.Validation("Ar.PromiseInvalid", "A promise needs a positive amount and a date no earlier than when it was made.");
        }

        if (!await _db.Customers.AnyAsync(c => c.Id == customerId && c.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Ar.CustomerNotFound", $"Customer {customerId} not found.");
        }

        if (await _db.Set<PaymentPromise>().AnyAsync(p => p.CustomerId == customerId && p.Status == PromiseStatus.Open, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Ar.PromiseOpen", "The customer already has an open promise; review it before recording another.");
        }

        PaymentPromise promise = new(companyId, customerId, madeOn, payBy, amount, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), recordedByUserId);
        _db.Set<PaymentPromise>().Add(promise);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return promise.Id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PromiseDiaryEntry>> DiaryAsync(long companyId, PromiseStatus? status = null, CancellationToken ct = default)
        => await (
            from p in _db.Set<PaymentPromise>().AsNoTracking()
            join c in _db.Customers.AsNoTracking() on p.CustomerId equals c.Id
            where p.CompanyId == companyId && (status == null || p.Status == status)
            orderby p.PayBy, p.Id
            select new PromiseDiaryEntry(p.Id, p.CustomerId, c.Code, p.MadeOn, p.PayBy, p.Amount, p.Status, p.Note))
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<PromiseReview> ReviewAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default)
    {
        List<PaymentPromise> open = await _db.Set<PaymentPromise>()
            .Where(p => p.CompanyId == companyId && p.Status == PromiseStatus.Open && p.MadeOn <= asAtDate)
            .ToListAsync(ct).ConfigureAwait(false);
        HashSet<long> customerIds = [.. open.Select(p => p.CustomerId)];
        var receipts = await _db.OpenItems.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.ItemType == OpenItemType.Receipt
                        && i.Sign == LedgerSign.Credit && customerIds.Contains(i.TraderId) && i.DocumentDate <= asAtDate)
            .Select(i => new { i.TraderId, i.DocumentDate, i.OriginalBase })
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> codes = await _db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Code, ct).ConfigureAwait(false);

        List<PromiseDiaryEntry> kept = [];
        List<PromiseDiaryEntry> broken = [];
        foreach (PaymentPromise promise in open)
        {
            DateOnly until = promise.PayBy < asAtDate ? promise.PayBy : asAtDate;
            decimal received = receipts.Where(r => r.TraderId == promise.CustomerId && r.DocumentDate >= promise.MadeOn && r.DocumentDate <= until).Sum(r => r.OriginalBase);
            if (!promise.Review(received, asAtDate))
            {
                continue;
            }

            PromiseDiaryEntry entry = new(promise.Id, promise.CustomerId, codes.GetValueOrDefault(promise.CustomerId, string.Empty), promise.MadeOn, promise.PayBy, promise.Amount, promise.Status, promise.Note);
            (promise.Status == PromiseStatus.Kept ? kept : broken).Add(entry);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new PromiseReview(kept, broken, open.Count - kept.Count - broken.Count);
    }
}
