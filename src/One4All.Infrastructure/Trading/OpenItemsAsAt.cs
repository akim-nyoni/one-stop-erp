using Microsoft.EntityFrameworkCore;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>An open item as it stood at a date.</summary>
/// <param name="Item">The item.</param>
/// <param name="OutstandingBase">Its outstanding base amount at the date (non-negative, like the stored field).</param>
/// <param name="OutstandingTransaction">Its outstanding amount in its own currency at the date.</param>
internal readonly record struct OpenItemAsAt(OpenItem Item, decimal OutstandingBase, decimal OutstandingTransaction)
{
    /// <summary>The outstanding amount signed so that <paramref name="positive"/> items count positive.</summary>
    /// <param name="positive">The sign that counts positive (debit for receivables, credit for payables).</param>
    /// <returns>The signed outstanding base amount.</returns>
    public decimal Signed(LedgerSign positive) => Item.Sign == positive ? OutstandingBase : -OutstandingBase;

    /// <summary>The outstanding amount in the item's own currency, signed so that <paramref name="positive"/> items count positive.</summary>
    /// <param name="positive">The sign that counts positive.</param>
    /// <returns>The signed outstanding transaction amount.</returns>
    public decimal SignedTransaction(LedgerSign positive) => Item.Sign == positive ? OutstandingTransaction : -OutstandingTransaction;
}

/// <summary>
/// Rebuilds trading open items as they stood at a past date. Outstanding amounts only ever change through
/// allocations (and their reversals), so an item's outstanding at a date is its original amount less the
/// allocations made by then that had not been reversed by then. Every "as at" trading report uses this
/// rather than the stored <see cref="OpenItem.OutstandingBase"/>, which is the position today.
/// </summary>
internal static class OpenItemsAsAt
{
    /// <summary>Loads the items of a ledger dated on or before a date with their outstanding at that date.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="companyId">The company.</param>
    /// <param name="ledger">The sub-ledger.</param>
    /// <param name="asAtDate">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The items still outstanding at the date (zero balances are left out).</returns>
    public static async Task<List<OpenItemAsAt>> LoadAsync(One4AllDbContext db, long companyId, TradingLedger ledger, DateOnly asAtDate, CancellationToken ct)
    {
        List<OpenItem> items = await db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == ledger && i.DocumentDate <= asAtDate)
            .ToListAsync(ct).ConfigureAwait(false);

        var allocations = await db.Allocations
            .Where(a => a.CompanyId == companyId && a.Ledger == ledger && a.AllocationDate <= asAtDate
                && !(a.IsReversed && a.ReversedOn != null && a.ReversedOn <= asAtDate))
            .Select(a => new { a.DebitItemId, a.CreditItemId, a.DebitBase, a.CreditBase, a.TransactionAmount })
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<long, decimal> drawn = [];
        Dictionary<long, decimal> drawnTransaction = [];
        foreach (var a in allocations)
        {
            drawn[a.DebitItemId] = drawn.GetValueOrDefault(a.DebitItemId) + a.DebitBase;
            drawn[a.CreditItemId] = drawn.GetValueOrDefault(a.CreditItemId) + a.CreditBase;
            drawnTransaction[a.DebitItemId] = drawnTransaction.GetValueOrDefault(a.DebitItemId) + a.TransactionAmount;
            drawnTransaction[a.CreditItemId] = drawnTransaction.GetValueOrDefault(a.CreditItemId) + a.TransactionAmount;
        }

        return items
            .Select(i => new OpenItemAsAt(
                i,
                decimal.Round(i.OriginalBase - drawn.GetValueOrDefault(i.Id), 4, MidpointRounding.ToEven),
                decimal.Round(i.OriginalTransaction - drawnTransaction.GetValueOrDefault(i.Id), 4, MidpointRounding.ToEven)))
            .Where(x => x.OutstandingBase != 0m)
            .ToList();
    }
}
