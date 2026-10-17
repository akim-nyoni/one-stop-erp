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
        // As the items stood at the date: allocations made afterwards must not age a past report.
        List<OpenItemAsAt> items = await OpenItemsAsAt.LoadAsync(_db, companyId, ledger, asAtDate, ct).ConfigureAwait(false);

        Dictionary<long, (string Code, string Name)> traders = await LoadTradersAsync(companyId, ledger, ct).ConfigureAwait(false);
        Dictionary<long, AgeingBasis> bases = await LoadAgeingBasesAsync(companyId, ledger, ct).ConfigureAwait(false);

        // The "balance owed" is debit-positive for receivables and credit-positive for payables.
        LedgerSign positiveSign = ledger == TradingLedger.AccountsReceivable ? LedgerSign.Debit : LedgerSign.Credit;

        List<AgeingRow> rows = items
            .GroupBy(i => i.Item.TraderId)
            .Select(g =>
            {
                decimal current = 0m, d30 = 0m, d60 = 0m, d90 = 0m, d90plus = 0m;
                foreach (OpenItemAsAt entry in g)
                {
                    decimal signed = entry.Signed(positiveSign);
                    // Each trader ages by its own basis: days past due, or days since the invoice (thirteenth audit #12).
                    DateOnly from = bases.GetValueOrDefault(g.Key) == AgeingBasis.InvoiceDate ? entry.Item.DocumentDate : entry.Item.DueDate;
                    int age = asAtDate.DayNumber - from.DayNumber;
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
        return new TraderStatement(customer.Code, customer.Name, customer.Currency.Value, lines, balance, customer.AgeingBasis);
    }

    public async Task<TraderStatement?> SupplierStatementAsync(long companyId, long supplierId, CancellationToken ct = default)
    {
        Supplier? supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (supplier is null)
        {
            return null;
        }

        List<OpenItem> items = await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable && i.TraderId == supplierId && i.OutstandingTransaction != 0m)
            .OrderBy(i => i.DocumentDate).ThenBy(i => i.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        // For a payable, a credit (invoice) is the positive balance; a debit (payment/credit note) reduces it.
        List<StatementLineRow> lines = items
            .Select(i => new StatementLineRow(
                i.DocumentDate, i.ItemType, i.DocumentNumber, i.DueDate,
                i.Sign == LedgerSign.Credit ? i.OriginalTransaction : -i.OriginalTransaction,
                i.Sign == LedgerSign.Credit ? i.OutstandingTransaction : -i.OutstandingTransaction))
            .ToList();

        decimal balance = lines.Sum(l => l.Outstanding);
        return new TraderStatement(supplier.Code, supplier.Name, supplier.Currency.Value, lines, balance, supplier.AgeingBasis);
    }

    public async Task<RemittanceAdvice?> RemittanceAdviceAsync(long companyId, long paymentOpenItemId, CancellationToken ct = default)
    {
        OpenItem? payment = await _db.OpenItems.FirstOrDefaultAsync(
            i => i.Id == paymentOpenItemId && i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable && i.ItemType == OpenItemType.Payment, ct).ConfigureAwait(false);
        if (payment is null)
        {
            return null;
        }

        Supplier? supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == payment.TraderId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (supplier is null)
        {
            return null;
        }

        // The payment is the debit item on AP allocations; the credit items are the invoices it settled.
        var rows = await (
            from a in _db.Set<Allocation>()
            join inv in _db.OpenItems on a.CreditItemId equals inv.Id
            where a.CompanyId == companyId && a.Ledger == TradingLedger.AccountsPayable && a.DebitItemId == paymentOpenItemId && !a.IsReversed
            orderby inv.DocumentDate, inv.DocumentNumber
            select new { inv.DocumentNumber, inv.DocumentDate, a.TransactionAmount })
            .ToListAsync(ct).ConfigureAwait(false);

        List<RemittanceLine> lines = rows.Select(r => new RemittanceLine(r.DocumentNumber, r.DocumentDate, r.TransactionAmount)).ToList();
        return new RemittanceAdvice(supplier.Code, supplier.Name, payment.DocumentNumber, payment.DocumentDate, payment.Currency.Value, lines, lines.Sum(l => l.AllocatedAmount));
    }

    /// <inheritdoc />
    public async Task<CreditExposureReport> CreditExposureAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default)
    {
        List<Customer> customers = await _db.Customers
            .Where(c => c.CompanyId == companyId)
            .ToListAsync(ct).ConfigureAwait(false);

        // Receivable balance per customer as at the date: debit items positive, credit items negative.
        Dictionary<long, decimal> balances = (await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, asAtDate, ct).ConfigureAwait(false))
            .GroupBy(i => i.Item.TraderId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Signed(LedgerSign.Debit)));

        List<CreditExposureRow> rows = customers
            .Select(c =>
            {
                decimal balance = balances.GetValueOrDefault(c.Id, 0m);
                bool unlimited = c.CreditLimit == 0m;
                decimal available = unlimited ? 0m : c.CreditLimit - balance;
                bool overLimit = !unlimited && balance > c.CreditLimit;
                return new CreditExposureRow(c.Code, c.Name, c.CreditLimit, balance, available, c.OnHold, overLimit);
            })
            .Where(r => r.Balance != 0m || r.CreditLimit != 0m || r.OnHold)
            .OrderBy(r => r.CustomerCode, StringComparer.Ordinal)
            .ToList();

        return new CreditExposureReport(asAtDate, rows);
    }

    /// <inheritdoc />
    public async Task<CashFlowForecastReport> CashFlowForecastAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default)
    {
        List<OpenItem> items = await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.OutstandingBase != 0m
                && (i.Ledger == TradingLedger.AccountsReceivable || i.Ledger == TradingLedger.AccountsPayable))
            .ToListAsync(ct).ConfigureAwait(false);

        string[] labels = ["Overdue", "Due 0-30", "Due 31-60", "Due 61-90", "Due 90+"];
        decimal[] inflow = new decimal[labels.Length];
        decimal[] outflow = new decimal[labels.Length];

        foreach (OpenItem item in items)
        {
            int daysAhead = item.DueDate.DayNumber - asAtDate.DayNumber;
            int bucket = daysAhead < 0 ? 0 : daysAhead <= 30 ? 1 : daysAhead <= 60 ? 2 : daysAhead <= 90 ? 3 : 4;

            if (item.Ledger == TradingLedger.AccountsReceivable)
            {
                // A debtor invoice (debit) is an inflow; a credit reduces it.
                inflow[bucket] += item.Sign == LedgerSign.Debit ? item.OutstandingBase : -item.OutstandingBase;
            }
            else
            {
                // A creditor invoice (credit) is an outflow; a debit reduces it.
                outflow[bucket] += item.Sign == LedgerSign.Credit ? item.OutstandingBase : -item.OutstandingBase;
            }
        }

        List<CashFlowForecastRow> rows = [];
        decimal running = 0m;
        for (int b = 0; b < labels.Length; b++)
        {
            decimal net = inflow[b] - outflow[b];
            running += net;
            rows.Add(new CashFlowForecastRow(labels[b], inflow[b], outflow[b], net, running));
        }

        return new CashFlowForecastReport(asAtDate, rows);
    }

    /// <inheritdoc />
    public async Task<PaymentsDueReport> PaymentsDueAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default)
    {
        List<OpenItem> invoices = await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable
                && i.ItemType == OpenItemType.Invoice && i.OutstandingBase != 0m)
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<long, (string Code, string Name)> suppliers = await _db.Suppliers
            .Where(s => s.CompanyId == companyId)
            .ToDictionaryAsync(s => s.Id, s => (s.Code, s.Name), ct).ConfigureAwait(false);

        List<PaymentDueRow> rows = invoices
            .Select(i =>
            {
                (string code, string name) = suppliers.GetValueOrDefault(i.TraderId, ($"#{i.TraderId}", string.Empty));
                // A creditor invoice is credit-sign; the outstanding payable is that credit balance.
                decimal outstanding = i.Sign == LedgerSign.Credit ? i.OutstandingBase : -i.OutstandingBase;
                return new PaymentDueRow(code, name, i.DocumentNumber, i.DocumentDate, i.DueDate, outstanding, i.DueDate.DayNumber - asAtDate.DayNumber);
            })
            .Where(r => r.Outstanding > 0m)
            .OrderBy(r => r.DueDate).ThenBy(r => r.SupplierCode, StringComparer.Ordinal)
            .ToList();

        return new PaymentsDueReport(asAtDate, rows, rows.Sum(r => r.Outstanding));
    }

    private async Task<Dictionary<long, AgeingBasis>> LoadAgeingBasesAsync(long companyId, TradingLedger ledger, CancellationToken ct)
        => ledger == TradingLedger.AccountsReceivable
            ? await _db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId).ToDictionaryAsync(c => c.Id, c => c.AgeingBasis, ct).ConfigureAwait(false)
            : await _db.Suppliers.AsNoTracking().Where(s => s.CompanyId == companyId).ToDictionaryAsync(s => s.Id, s => s.AgeingBasis, ct).ConfigureAwait(false);

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
