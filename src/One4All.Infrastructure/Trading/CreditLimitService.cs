using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>The credit limit approval workflow.</summary>
public sealed class CreditLimitService : ICreditLimitService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public CreditLimitService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> LowerAsync(long companyId, long customerId, decimal limit, CancellationToken ct = default)
    {
        Customer? customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return Result.Failure(Error.NotFound("Ar.CustomerNotFound", $"Customer {customerId} not found."));
        }

        if (limit <= 0m || limit == customer.CreditLimit || CreditLimitRequest.IsIncrease(customer.CreditLimit, limit))
        {
            return Result.Failure(Error.Rule("Ar.NotALowering", "That would not lower the limit; raising a limit needs an approved request."));
        }

        customer.LowerCreditLimit(limit);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> RequestIncreaseAsync(long companyId, long customerId, decimal limit, string reason, long requestedByUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || limit < 0m)
        {
            return Error.Validation("Ar.LimitRequestInvalid", "A credit limit request needs a reason and a limit of 0 (none) or more.");
        }

        Customer? customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return Error.NotFound("Ar.CustomerNotFound", $"Customer {customerId} not found.");
        }

        if (!CreditLimitRequest.IsIncrease(customer.CreditLimit, limit))
        {
            return Error.Rule("Ar.NotAnIncrease", "That does not raise the limit; lower it directly instead.");
        }

        if (await _db.Set<CreditLimitRequest>().AnyAsync(r => r.CustomerId == customerId && r.Status == CreditLimitRequestStatus.Pending, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Ar.LimitRequestPending", $"Customer {customer.Code} already has a credit limit request awaiting approval.");
        }

        CreditLimitRequest request = new(companyId, customerId, customer.CreditLimit, limit, reason.Trim(), requestedByUserId);
        _db.Set<CreditLimitRequest>().Add(request);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return request.Id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PendingCreditLimit>> PendingAsync(long companyId, CancellationToken ct = default)
        => await (
            from r in _db.Set<CreditLimitRequest>().AsNoTracking()
            join c in _db.Customers.AsNoTracking() on r.CustomerId equals c.Id
            where r.CompanyId == companyId && r.Status == CreditLimitRequestStatus.Pending
            orderby r.Id
            select new PendingCreditLimit(r.Id, r.CustomerId, c.Code, r.CurrentLimit, r.RequestedLimit, r.Reason, r.RequestedByUserId))
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Result> ApproveAsync(long companyId, long requestId, long approverUserId, CancellationToken ct = default)
    {
        CreditLimitRequest? request = await _db.Set<CreditLimitRequest>().FirstOrDefaultAsync(r => r.Id == requestId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (request is null || request.Status != CreditLimitRequestStatus.Pending)
        {
            return Result.Failure(Error.Rule("Ar.LimitRequestNotPending", $"Credit limit request {requestId} is not awaiting approval."));
        }

        if (!request.Approve(approverUserId))
        {
            return Result.Failure(Error.Rule("Ar.SelfApproval", "A different user must approve a credit limit increase than the one who asked for it."));
        }

        Customer customer = await _db.Customers.FirstAsync(c => c.Id == request.CustomerId, ct).ConfigureAwait(false);
        customer.RaiseCreditLimit(request);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> RejectAsync(long companyId, long requestId, long userId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Ar.ReasonRequired", "Rejecting a credit limit request requires a reason."));
        }

        CreditLimitRequest? request = await _db.Set<CreditLimitRequest>().FirstOrDefaultAsync(r => r.Id == requestId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (request is null || !request.Reject(userId, reason.Trim()))
        {
            return Result.Failure(Error.Rule("Ar.LimitRequestNotPending", $"Credit limit request {requestId} is not awaiting approval."));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
