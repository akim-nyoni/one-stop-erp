using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>
/// Credit control: the review that places over-limit / overdue customers on stop, and the hand hold and
/// release. Balances are read from the AR open items in base currency (debits less credits, the same
/// measure the invoice-time credit check uses); overdue is measured on the open item's due date.
/// </summary>
public sealed class CreditControlService : ICreditControlService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public CreditControlService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<CreditReviewResult>> ReviewAsync(long companyId, DateOnly asAt, int overdueToleranceDays, CancellationToken ct = default)
    {
        if (overdueToleranceDays < 0)
        {
            return Error.Validation("Credit.Tolerance", "The overdue tolerance cannot be negative.");
        }

        List<Customer> candidates = await _db.Customers
            .Where(c => c.CompanyId == companyId && c.IsActive && !c.OnHold)
            .OrderBy(c => c.Code)
            .ToListAsync(ct).ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return new CreditReviewResult(asAt, overdueToleranceDays, 0, []);
        }

        List<long> ids = candidates.Select(c => c.Id).ToList();
        var items = await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && ids.Contains(i.TraderId) && i.OutstandingBase != 0m)
            .Select(i => new { i.TraderId, i.Sign, i.OutstandingBase, i.DueDate })
            .ToListAsync(ct).ConfigureAwait(false);

        DateOnly overdueCutoff = asAt.AddDays(-overdueToleranceDays);
        List<CreditHold> held = [];
        foreach (Customer customer in candidates)
        {
            var mine = items.Where(i => i.TraderId == customer.Id).ToList();
            decimal balance = mine.Sum(i => i.Sign == LedgerSign.Debit ? i.OutstandingBase : -i.OutstandingBase);

            // Overdue: debit items whose due date is earlier than the cutoff (strictly beyond the tolerance).
            var overdue = mine.Where(i => i.Sign == LedgerSign.Debit && i.DueDate < overdueCutoff).ToList();
            decimal overdueAmount = overdue.Sum(i => i.OutstandingBase);
            int oldestDays = overdue.Count == 0 ? 0 : overdue.Max(i => asAt.DayNumber - i.DueDate.DayNumber);

            bool overLimit = customer.CreditLimit > 0m && balance > customer.CreditLimit;
            bool isOverdue = overdueAmount > 0m;
            if (!overLimit && !isOverdue)
            {
                continue;
            }

            CreditHoldReason reason = overLimit && isOverdue ? CreditHoldReason.OverLimitAndOverdue
                : overLimit ? CreditHoldReason.OverLimit
                : CreditHoldReason.Overdue;

            customer.OnHold = true;
            held.Add(new CreditHold(customer.Id, customer.Code, customer.Name, reason, balance, customer.CreditLimit, overdueAmount, oldestDays));
        }

        if (held.Count > 0)
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new CreditReviewResult(asAt, overdueToleranceDays, candidates.Count, held);
    }

    /// <inheritdoc />
    public async Task<Result> HoldAsync(long companyId, long customerId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Credit.ReasonRequired", "A reason is required to place a customer on hold."));
        }

        Customer? customer = await FindAsync(companyId, customerId, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return Result.Failure(Error.NotFound("Ar.CustomerNotFound", $"Customer {customerId} not found."));
        }

        if (customer.OnHold)
        {
            return Result.Failure(Error.Rule("Credit.AlreadyOnHold", $"Customer {customer.Code} is already on hold."));
        }

        customer.OnHold = true;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> ReleaseAsync(long companyId, long customerId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Credit.ReasonRequired", "A reason is required to release a customer from hold."));
        }

        Customer? customer = await FindAsync(companyId, customerId, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return Result.Failure(Error.NotFound("Ar.CustomerNotFound", $"Customer {customerId} not found."));
        }

        if (!customer.OnHold)
        {
            return Result.Failure(Error.Rule("Credit.NotOnHold", $"Customer {customer.Code} is not on hold."));
        }

        customer.OnHold = false;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private Task<Customer?> FindAsync(long companyId, long customerId, CancellationToken ct) =>
        _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId && c.CompanyId == companyId, ct);
}
