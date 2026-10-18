using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>
/// Renders statements of account to text: takes the open-item statement from the trading reports and
/// formats it under the company heading with an ageing footer.
/// </summary>
public sealed class StatementService : IStatementService
{
    private readonly One4AllDbContext _db;
    private readonly ITradingReports _reports;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="reports">The trading reports (supply the statement data).</param>
    public StatementService(One4AllDbContext db, ITradingReports reports)
    {
        _db = db;
        _reports = reports;
    }

    /// <inheritdoc />
    public async Task<Result<string>> RenderCustomerStatementTextAsync(long companyId, long customerId, DateOnly asAt, int width = StatementFormatter.DefaultWidth, CancellationToken ct = default)
    {
        if (await _db.Customers.AnyAsync(c => c.Id == customerId && c.CompanyId == companyId && c.StatementType == StatementType.BalanceForward, ct).ConfigureAwait(false))
        {
            return await RenderCustomerBalanceForwardTextAsync(companyId, customerId, new DateOnly(asAt.Year, asAt.Month, 1), asAt, width, ct).ConfigureAwait(false);
        }

        TraderStatement? statement = await _reports.CustomerStatementAsync(companyId, customerId, ct).ConfigureAwait(false);
        return statement is null
            ? Error.NotFound("Ar.CustomerNotFound", $"Customer {customerId} not found.")
            : await RenderAsync(companyId, statement, asAt, width, TradingLedger.AccountsReceivable, customerId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<string>> RenderSupplierStatementTextAsync(long companyId, long supplierId, DateOnly asAt, int width = StatementFormatter.DefaultWidth, CancellationToken ct = default)
    {
        TraderStatement? statement = await _reports.SupplierStatementAsync(companyId, supplierId, ct).ConfigureAwait(false);
        return statement is null
            ? Error.NotFound("Ap.SupplierNotFound", $"Supplier {supplierId} not found.")
            : await RenderAsync(companyId, statement, asAt, width, TradingLedger.AccountsPayable, supplierId, ct).ConfigureAwait(false);
    }

    public async Task<Result<string>> RenderCustomerBalanceForwardTextAsync(long companyId, long customerId, DateOnly from, DateOnly to, int width = StatementFormatter.DefaultWidth,
        CancellationToken ct = default)
    {
        if (from > to)
        {
            return Error.Validation("Ar.StatementPeriod", "The statement period must start on or before it ends.");
        }

        Customer? customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return Error.NotFound("Ar.CustomerNotFound", $"Customer {customerId} not found.");
        }

        // Every document (invoice, credit note, receipt, journal) is an open item, so the brought-forward balance is the signed total of those
        // dated before the period, whatever has since been allocated.
        var items = await _db.OpenItems.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customerId && i.DocumentDate <= to && i.Currency == customer.Currency)
            .OrderBy(i => i.DocumentDate).ThenBy(i => i.Id)
            .Select(i => new { i.DocumentDate, i.ItemType, i.DocumentNumber, Amount = i.Sign == LedgerSign.Debit ? i.OriginalTransaction : -i.OriginalTransaction })
            .ToListAsync(ct).ConfigureAwait(false);
        decimal opening = items.Where(i => i.DocumentDate < from).Sum(i => i.Amount);
        decimal running = opening;
        List<BalanceForwardLine> lines = [];
        foreach (var item in items.Where(i => i.DocumentDate >= from))
        {
            running += item.Amount;
            lines.Add(new BalanceForwardLine(item.DocumentDate, item.ItemType, item.DocumentNumber, item.Amount, running));
        }

        TraderStatement? openItems = await _reports.CustomerStatementAsync(companyId, customerId, ct).ConfigureAwait(false);
        StatementAgeing ageing = StatementAgeing.Of(openItems!, to);
        BalanceForwardStatement statement = new(customer.Code, customer.Name, customer.Currency.Value, from, to, opening, lines, running, ageing);
        string companyName = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;
        return StatementFormatter.ToBalanceForwardText(statement, companyName, width,
            await TraderDirectory.CorrespondenceAsync(_db, companyId, TradingLedger.AccountsReceivable, customerId, ct).ConfigureAwait(false), remittanceSlip: true);
    }

    private async Task<Result<string>> RenderAsync(long companyId, TraderStatement statement, DateOnly asAt, int width, TradingLedger ledger, long traderId, CancellationToken ct)
    {
        string companyName = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;
        return StatementFormatter.ToPlainText(statement, companyName, asAt, width, await TraderDirectory.CorrespondenceAsync(_db, companyId, ledger, traderId, ct).ConfigureAwait(false),
            remittanceSlip: ledger == TradingLedger.AccountsReceivable);
    }
}
