using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>Sales rep commission over the AR ledger and its invoice journals.</summary>
public sealed class CommissionService : ICommissionService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _engine;
    private readonly Application.Abstractions.Payroll.IPayrollVariableService? _variables;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="engine">The posting engine.</param>
    /// <param name="variables">The payroll variables commission is sent to (null: not paid through payroll).</param>
    public CommissionService(One4AllDbContext db, IPostingEngine engine, Application.Abstractions.Payroll.IPayrollVariableService? variables = null)
    {
        _db = db;
        _engine = engine;
        _variables = variables;
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<CommissionPayrollLine>>> ToPayrollAsync(long companyId, DateOnly from, DateOnly to, string periodLabel, string elementCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => ToPayrollCoreAsync(companyId, from, to, periodLabel, elementCode, ct), ct);

    // One unit: every rep's variable is set, or none.
    private async Task<Result<IReadOnlyList<CommissionPayrollLine>>> ToPayrollCoreAsync(long companyId, DateOnly from, DateOnly to, string periodLabel, string elementCode, CancellationToken ct)
    {
        if (_variables is null)
        {
            return Error.Rule("Commission.NoPayroll", "Payroll is not available here.");
        }

        if (to < from || string.IsNullOrWhiteSpace(periodLabel) || string.IsNullOrWhiteSpace(elementCode))
        {
            return Error.Validation("Commission.PayrollPeriod", "Sending commission to payroll needs the period it was earned in, the pay period and the commission element.");
        }

        var reps = await _db.Set<SalesRep>().AsNoTracking().Where(r => r.CompanyId == companyId && r.IsActive && r.EmployeeId != null)
            .Select(r => new { r.Id, r.Code, EmployeeId = r.EmployeeId!.Value, Number = _db.Employees.Where(e => e.Id == r.EmployeeId).Select(e => e.EmployeeNumber).FirstOrDefault() })
            .OrderBy(r => r.Code).ToListAsync(ct).ConfigureAwait(false);
        List<CommissionPayrollLine> lines = [];
        foreach (var rep in reps)
        {
            Result<CommissionStatement> statement = await StatementAsync(companyId, rep.Id, from, to, ct).ConfigureAwait(false);
            if (statement.IsFailure)
            {
                return statement.Error;
            }

            decimal amount = decimal.Round(statement.Value.Total, 2, MidpointRounding.AwayFromZero);
            if (amount <= 0m)
            {
                continue;
            }

            Result<long> set = await _variables.SetAsync(companyId, rep.EmployeeId, elementCode.Trim(), periodLabel.Trim(), null, amount, ct).ConfigureAwait(false);
            if (set.IsFailure)
            {
                return set.Error;
            }

            lines.Add(new CommissionPayrollLine(rep.Code, rep.Number ?? string.Empty, amount));
        }

        return lines;
    }

    /// <inheritdoc />
    public async Task<Result<CommissionStatement>> StatementAsync(long companyId, long salesRepId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        SalesRep? rep = await _db.Set<SalesRep>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == salesRepId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (rep is null)
        {
            return Error.NotFound("Commission.RepNotFound", $"Sales rep {salesRepId} not found.");
        }

        return await StatementForAsync(rep, from, to, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<CommissionAccrualLine>>> AccrueAsync(long companyId, DateOnly monthEnd, string expenseAccountCode, string payableAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => AccrueCoreAsync(companyId, monthEnd, expenseAccountCode, payableAccountCode, ct), ct);

    // One unit: the journal and every rep's accrual record are kept together.
    private async Task<Result<IReadOnlyList<CommissionAccrualLine>>> AccrueCoreAsync(long companyId, DateOnly monthEnd, string expenseAccountCode, string payableAccountCode, CancellationToken ct)
    {
        DateOnly first = new(monthEnd.Year, monthEnd.Month, 1);
        DateOnly last = first.AddMonths(1).AddDays(-1);
        List<SalesRep> reps = await _db.Set<SalesRep>().AsNoTracking().Where(r => r.CompanyId == companyId && r.IsActive).OrderBy(r => r.Code).ToListAsync(ct).ConfigureAwait(false);
        List<long> repIds = [.. reps.Select(r => r.Id)];
        Dictionary<long, CommissionAccrual> accruals = await _db.Set<CommissionAccrual>()
            .Where(a => a.CompanyId == companyId && a.Month == first && repIds.Contains(a.SalesRepId))
            .ToDictionaryAsync(a => a.SalesRepId, ct).ConfigureAwait(false);

        List<CommissionAccrualLine> lines = [];
        List<(CommissionAccrual Accrual, decimal Delta)> changes = [];
        foreach (SalesRep rep in reps)
        {
            CommissionStatement statement = await StatementForAsync(rep, first, last, ct).ConfigureAwait(false);
            CommissionAccrual? accrual = accruals.GetValueOrDefault(rep.Id);
            decimal previously = accrual?.Accrued ?? 0m;
            lines.Add(new CommissionAccrualLine(rep.Code, statement.Total, previously));
            decimal delta = statement.Total - previously;
            if (delta != 0m)
            {
                if (accrual is null)
                {
                    accrual = new CommissionAccrual(companyId, rep.Id, first);
                    _db.Set<CommissionAccrual>().Add(accrual);
                }

                changes.Add((accrual, delta));
            }
        }

        decimal net = changes.Sum(c => c.Delta);
        if (net != 0m)
        {
            CurrencyCode currency = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
            Result<PostingReceipt> posted = await _engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId,
                SourceModule = "AR",
                PostingDate = last,
                TransactionCurrency = currency,
                DocumentType = "CommissionAccrual",
                DocumentNumber = $"COM-{first:yyyyMM}",
                Narrative = $"Sales commission accrued for {first.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}",
                Lines =
                [
                    new JournalLineRequest { AccountCode = expenseAccountCode, Side = net > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(net) },
                    new JournalLineRequest { AccountCode = payableAccountCode, Side = net > 0m ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = Math.Abs(net) },
                ],
            }, ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }
        }

        foreach ((CommissionAccrual accrual, decimal delta) in changes)
        {
            accrual.Add(delta);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<IReadOnlyList<CommissionAccrualLine>>.Success(lines);
    }

    private async Task<CommissionStatement> StatementForAsync(SalesRep rep, DateOnly start, DateOnly end, CancellationToken ct)
    {
        Dictionary<long, string> customers = await _db.Customers.AsNoTracking()
            .Where(c => c.CompanyId == rep.CompanyId && c.SalesRepId == rep.Id)
            .ToDictionaryAsync(c => c.Id, c => c.Code, ct).ConfigureAwait(false);
        List<long> ids = [.. customers.Keys];

        Dictionary<long, decimal> bases;
        if (rep.Basis is CommissionBasis.Invoiced or CommissionBasis.Margin)
        {
            // Net revenue: the income lines of the customers' invoice and credit-note journals, so tax is left out.
            bases = await (
                from item in _db.OpenItems
                join line in _db.Set<JournalLine>() on item.JournalEntryId equals line.JournalEntryId
                join account in _db.Accounts on new { line.AccountCode, item.CompanyId } equals new { account.AccountCode, account.CompanyId }
                where item.CompanyId == rep.CompanyId && item.Ledger == TradingLedger.AccountsReceivable && ids.Contains(item.TraderId)
                      && (item.ItemType == OpenItemType.Invoice || item.ItemType == OpenItemType.CreditNote)
                      && item.DocumentDate >= start && item.DocumentDate <= end && account.Type == AccountType.Income
                group line by item.TraderId into g
                select new { CustomerId = g.Key, Amount = g.Sum(l => l.BaseCredit - l.BaseDebit) })
                .ToDictionaryAsync(x => x.CustomerId, x => x.Amount, ct).ConfigureAwait(false);

            if (rep.Basis == CommissionBasis.Margin)
            {
                // The cost of the goods despatched to each customer in the period (their sales orders' deliveries), unless the issue was reversed.
                var costs = await (
                    from delivery in _db.SalesDeliveries
                    join order in _db.Set<Core.SalesOrders.SalesOrder>() on delivery.SalesOrderId equals order.Id
                    join movement in _db.StockMovements on new { delivery.CompanyId, Reference = (string?)delivery.Reference } equals new { movement.CompanyId, movement.Reference }
                    where delivery.CompanyId == rep.CompanyId && ids.Contains(order.CustomerId) && delivery.DespatchedOn >= start && delivery.DespatchedOn <= end
                          && movement.Type == Core.Inventory.StockMovementType.Issue && !movement.IsReversed
                    group movement by order.CustomerId into g
                    select new { CustomerId = g.Key, Cost = g.Sum(m => m.Value) })
                    .ToListAsync(ct).ConfigureAwait(false);
                foreach (var cost in costs)
                {
                    bases[cost.CustomerId] = bases.GetValueOrDefault(cost.CustomerId) - Math.Abs(cost.Cost);
                }
            }
        }
        else
        {
            // Money collected: receipts, but not write-offs, which are posted as receipts against bad debt.
            var receipts = await _db.OpenItems
                .Where(i => i.CompanyId == rep.CompanyId && i.Ledger == TradingLedger.AccountsReceivable && ids.Contains(i.TraderId)
                            && i.ItemType == OpenItemType.Receipt && i.DocumentDate >= start && i.DocumentDate <= end)
                .Select(i => new { i.TraderId, i.DocumentNumber, i.OriginalBase })
                .ToListAsync(ct).ConfigureAwait(false);
            bases = receipts.Where(r => !IsWriteOff(r.DocumentNumber))
                .GroupBy(r => r.TraderId).ToDictionary(g => g.Key, g => g.Sum(r => r.OriginalBase));
        }

        List<CommissionLine> lines = [.. bases
            .OrderBy(b => customers[b.Key], StringComparer.Ordinal)
            .Select(b => new CommissionLine(b.Key, customers[b.Key], b.Value, decimal.Round(b.Value * rep.RatePercent / 100m, 2, MidpointRounding.AwayFromZero)))];
        return new CommissionStatement(rep.Id, rep.Code, start, end, lines);
    }

    // Write-offs are numbered WO{item} (and WOV{item} for their VAT relief).
    private static bool IsWriteOff(string documentNumber)
    {
        string digits = documentNumber.StartsWith("WOV", StringComparison.Ordinal) ? documentNumber[3..]
            : documentNumber.StartsWith("WO", StringComparison.Ordinal) ? documentNumber[2..] : string.Empty;
        return digits.Length > 0 && digits.All(char.IsAsciiDigit);
    }
}
