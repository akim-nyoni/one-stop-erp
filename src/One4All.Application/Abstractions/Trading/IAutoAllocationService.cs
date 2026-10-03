using One4All.Core.Common;
using One4All.Core.Trading;

namespace One4All.Application.Abstractions.Trading;

/// <summary>What an auto-allocation run did.</summary>
/// <param name="TradersProcessed">Traders that had both debit and credit items outstanding.</param>
/// <param name="Allocations">Allocations made.</param>
/// <param name="ExactMatches">Of those, how many paired identical amounts.</param>
/// <param name="AmountAllocated">Total transaction amount allocated (all currencies summed — a count of work, not a balance).</param>
/// <param name="Failures">Allocations the allocation service refused, with the reason (the run carries on past them).</param>
public sealed record AutoAllocationResult(int TradersProcessed, int Allocations, int ExactMatches, decimal AmountAllocated, IReadOnlyList<string> Failures);

/// <summary>
/// Automatic allocation (brief §4.5/§4.6): settles a trader's outstanding receipts / payments / credit notes
/// against their outstanding invoices — identical amounts first, then oldest first — through the ordinary
/// allocation service, so realised FX and the audit trail are exactly as for a manual allocation.
/// </summary>
public interface IAutoAllocationService
{
    /// <summary>Auto-allocates one trader's open items (per currency).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ledger">AR (customer) or AP (supplier).</param>
    /// <param name="traderId">The customer or supplier.</param>
    /// <param name="allocationDate">The allocation date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was allocated.</returns>
    Task<Result<AutoAllocationResult>> AllocateTraderAsync(long companyId, TradingLedger ledger, long traderId, DateOnly allocationDate, CancellationToken ct = default);

    /// <summary>Auto-allocates every trader on a ledger that has both debit and credit items outstanding.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ledger">AR or AP.</param>
    /// <param name="allocationDate">The allocation date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was allocated.</returns>
    Task<AutoAllocationResult> AllocateLedgerAsync(long companyId, TradingLedger ledger, DateOnly allocationDate, CancellationToken ct = default);
}
