using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>Plans allocations with <see cref="AllocationPlanner"/> and executes them through <see cref="IAllocationService"/>.</summary>
public sealed class AutoAllocationService : IAutoAllocationService
{
    private readonly One4AllDbContext _db;
    private readonly IAllocationService _allocation;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="allocation">The allocation service (does the actual matching, FX and audit).</param>
    public AutoAllocationService(One4AllDbContext db, IAllocationService allocation)
    {
        _db = db;
        _allocation = allocation;
    }

    /// <inheritdoc />
    public async Task<Result<AutoAllocationResult>> AllocateTraderAsync(long companyId, TradingLedger ledger, long traderId, DateOnly allocationDate, CancellationToken ct = default)
    {
        bool exists = ledger == TradingLedger.AccountsReceivable
            ? await _db.Customers.AnyAsync(c => c.Id == traderId && c.CompanyId == companyId, ct).ConfigureAwait(false)
            : await _db.Suppliers.AnyAsync(s => s.Id == traderId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (!exists)
        {
            return ledger == TradingLedger.AccountsReceivable
                ? Error.NotFound("Ar.CustomerNotFound", $"Customer {traderId} not found.")
                : Error.NotFound("Ap.SupplierNotFound", $"Supplier {traderId} not found.");
        }

        return await RunAsync(companyId, ledger, [traderId], allocationDate, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AutoAllocationResult> AllocateLedgerAsync(long companyId, TradingLedger ledger, DateOnly allocationDate, CancellationToken ct = default)
    {
        // Only traders with something on both sides can allocate anything.
        List<long> traders = await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == ledger && i.OutstandingTransaction > 0m)
            .GroupBy(i => i.TraderId)
            .Where(g => g.Any(i => i.Sign == LedgerSign.Debit) && g.Any(i => i.Sign == LedgerSign.Credit))
            .Select(g => g.Key)
            .OrderBy(id => id)
            .ToListAsync(ct).ConfigureAwait(false);

        return await RunAsync(companyId, ledger, traders, allocationDate, ct).ConfigureAwait(false);
    }

    private async Task<AutoAllocationResult> RunAsync(long companyId, TradingLedger ledger, IReadOnlyList<long> traderIds, DateOnly allocationDate, CancellationToken ct)
    {
        int processed = 0;
        int allocations = 0;
        int exact = 0;
        decimal total = 0m;
        List<string> failures = [];

        foreach (long traderId in traderIds)
        {
            var items = await _db.OpenItems
                .Where(i => i.CompanyId == companyId && i.Ledger == ledger && i.TraderId == traderId && i.OutstandingTransaction > 0m && i.DocumentDate <= allocationDate)
                .Select(i => new { i.Id, i.Sign, i.Currency, i.DocumentNumber, i.DocumentDate, i.DueDate, i.OutstandingTransaction })
                .ToListAsync(ct).ConfigureAwait(false);

            bool any = false;
            foreach (var currency in items.GroupBy(i => i.Currency))
            {
                List<AllocatableItem> debits = currency.Where(i => i.Sign == LedgerSign.Debit).Select(i => new AllocatableItem(i.Id, i.DocumentDate, i.DueDate, i.OutstandingTransaction)).ToList();
                List<AllocatableItem> credits = currency.Where(i => i.Sign == LedgerSign.Credit).Select(i => new AllocatableItem(i.Id, i.DocumentDate, i.DueDate, i.OutstandingTransaction)).ToList();
                if (debits.Count == 0 || credits.Count == 0)
                {
                    continue;
                }

                any = true;
                Dictionary<long, string> numbers = currency.ToDictionary(i => i.Id, i => i.DocumentNumber);
                foreach (PlannedAllocation planned in AllocationPlanner.Plan(debits, credits))
                {
                    Result<AllocationResult> done = await _allocation.AllocateAsync(companyId, ledger, planned.DebitItemId, planned.CreditItemId, planned.Amount, allocationDate, ct).ConfigureAwait(false);
                    if (done.IsFailure)
                    {
                        failures.Add($"{numbers[planned.CreditItemId]} → {numbers[planned.DebitItemId]}: {done.Error.Message}");
                        continue;
                    }

                    allocations++;
                    total += done.Value.TransactionAmount;
                    if (planned.ExactMatch)
                    {
                        exact++;
                    }
                }
            }

            if (any)
            {
                processed++;
            }
        }

        return new AutoAllocationResult(processed, allocations, exact, total, failures);
    }
}
