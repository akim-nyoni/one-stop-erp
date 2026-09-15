using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>Aged-balance and statement reports for the trader ledgers (brief §4.5/§4.6, §14).</summary>
public sealed class TradingReports : ITradingReports
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the report service.</summary>
    /// <param name="db">The database context.</param>
    public TradingReports(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public Task<AgeingReport> AgedDebtorsAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default) =>
        AgeAsync(companyId, TradingLedger.AccountsReceivable, asAtDate, ct);

    /// <inheritdoc />
    public Task<AgeingReport> AgedCreditorsAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default) =>
        AgeAsync(companyId, TradingLedger.AccountsPayable, asAtDate, ct);

    private async Task<AgeingReport> AgeAsync(long companyId, TradingLedger ledger, DateOnly asAtDate, CancellationToken ct)
    {
        List<OpenItem> items = await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == ledger && i.OutstandingBase != 0m && i.DocumentDate <= asAtDate)
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<long, (string Code, string Name)> traders = await LoadTradersAsync(companyId, ledger, ct).ConfigureAwait(false);

        // The "balance owed" is debit-positive for receivables and credit-positive for payables.
        LedgerSign positiveSign = ledger == TradingLedger.AccountsReceivable ? LedgerSign.Debit : LedgerSign.Credit;

        List<AgeingRow> rows = items
            .GroupBy(i => i.TraderId)
            .Select(g =>
            {
                decimal current = 0m, d30 = 0m, d60 = 0m, d90 = 0m, d90plus = 0m;
                foreach (OpenItem item in g)
                {
                    decimal signed = item.Sign == positiveSign ? item.OutstandingBase : -item.OutstandingBase;
                    int age = asAtDate.DayNumber - item.DueDate.DayNumber;
                    if (age <= 0) { current += signed; }
                    else if (age <= 30) { d30 += signed; }
                    else if (age <= 60) { d60 += signed; }
                    else if (age <= 90) { d90 += signed; }
                    else { d90plus += signed; }
                }

                (string code, string name) = traders.GetValueOrDefault(g.Key, ($"#{g.Key}", string.Empty));
                return new AgeingRow(code, name, current, d30, d60, d90, d90plus);
            })
            .Where(r => r.Total != 0m)
            .OrderBy(r => r.TraderCode, StringComparer.Ordinal)
            .ToList();

        return new AgeingReport(ledger, asAtDate, rows);
    }

    /// <inheritdoc />
    public async Task<TraderStatement?> CustomerStatementAsync(long companyId, long customerId, CancellationToken ct = default)
    {
        Customer? customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return null;
        }

        List<OpenItem> items = await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customerId && i.OutstandingTransaction != 0m)
            .OrderBy(i => i.DocumentDate).ThenBy(i => i.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        List<StatementLineRow> lines = items
            .Select(i => new StatementLineRow(
                i.DocumentDate, i.ItemType, i.DocumentNumber, i.DueDate,
                i.Sign == LedgerSign.Debit ? i.OriginalTransaction : -i.OriginalTransaction,
                i.Sign == LedgerSign.Debit ? i.OutstandingTransaction : -i.OutstandingTransaction))
            .ToList();

        decimal balance = lines.Sum(l => l.Outstanding);
        return new TraderStatement(customer.Code, customer.Name, customer.Currency.Value, lines, balance);
    }

    private async Task<Dictionary<long, (string, string)>> LoadTradersAsync(long companyId, TradingLedger ledger, CancellationToken ct)
    {
        if (ledger == TradingLedger.AccountsReceivable)
        {
            return await _db.Customers.Where(c => c.CompanyId == companyId)
                .ToDictionaryAsync(c => c.Id, c => (c.Code, c.Name), ct).ConfigureAwait(false);
        }

        return await _db.Suppliers.Where(s => s.CompanyId == companyId)
            .ToDictionaryAsync(s => s.Id, s => (s.Code, s.Name), ct).ConfigureAwait(false);
    }
}
