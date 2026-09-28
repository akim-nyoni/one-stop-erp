using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// Computes DSO and DPO from posted journals: the receivables and payables control balances as at the end
/// of the period, and the revenue and expense flows over the period, combined by <see cref="WorkingCapital"/>.
/// </summary>
public sealed class WorkingCapitalService : IWorkingCapitalService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public WorkingCapitalService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<WorkingCapitalMetrics>> ComputeAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        if (toDate < fromDate)
        {
            return Error.Validation("WorkingCapital.Period", "The end date must not be before the start date.");
        }

        List<LedgerAccount> accounts = await _db.Accounts.Where(a => a.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false);

        HashSet<string> arAccounts = accounts.Where(a => a.ControlAccountType == ControlAccountType.AccountsReceivable).Select(a => a.AccountCode).ToHashSet(StringComparer.Ordinal);
        HashSet<string> apAccounts = accounts.Where(a => a.ControlAccountType == ControlAccountType.AccountsPayable).Select(a => a.AccountCode).ToHashSet(StringComparer.Ordinal);
        HashSet<string> stockAccounts = accounts.Where(a => a.ControlAccountType == ControlAccountType.Stock).Select(a => a.AccountCode).ToHashSet(StringComparer.Ordinal);
        HashSet<string> incomeAccounts = accounts.Where(a => a.Type == AccountType.Income).Select(a => a.AccountCode).ToHashSet(StringComparer.Ordinal);
        HashSet<string> expenseAccounts = accounts.Where(a => a.Type == AccountType.Expense).Select(a => a.AccountCode).ToHashSet(StringComparer.Ordinal);

        // Cost-of-sales accounts are those the items post their issues to.
        HashSet<string> cogsAccounts = (await _db.Set<Core.Inventory.Item>()
            .Where(i => i.CompanyId == companyId && i.CostOfSalesAccountCode != null)
            .Select(i => i.CostOfSalesAccountCode!)
            .Distinct()
            .ToListAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);

        Dictionary<string, (decimal debit, decimal credit)> asAt = await SumByAccountAsync(companyId, null, toDate, ct).ConfigureAwait(false);
        Dictionary<string, (decimal debit, decimal credit)> period = await SumByAccountAsync(companyId, fromDate, toDate, ct).ConfigureAwait(false);

        decimal receivables = arAccounts.Sum(code => Net(asAt, code));          // AR is a debit balance
        decimal payables = apAccounts.Sum(code => -Net(asAt, code));            // AP is a credit balance
        decimal inventory = stockAccounts.Sum(code => Net(asAt, code));         // stock is a debit balance
        decimal creditSales = incomeAccounts.Sum(code => -Net(period, code));   // income is a credit movement
        // Purchases are operating expenses excluding cost of sales, which is measured separately by DIO.
        decimal creditPurchases = expenseAccounts.Where(code => !cogsAccounts.Contains(code)).Sum(code => Net(period, code));
        decimal costOfSales = cogsAccounts.Sum(code => Net(period, code));      // COGS is a debit movement

        int days = toDate.DayNumber - fromDate.DayNumber + 1;
        decimal dso = WorkingCapital.DaysOutstanding(receivables, creditSales, days);
        decimal dpo = WorkingCapital.DaysOutstanding(payables, creditPurchases, days);
        decimal dio = WorkingCapital.DaysOutstanding(inventory, costOfSales, days);

        return new WorkingCapitalMetrics(fromDate, toDate, receivables, creditSales, dso, payables, creditPurchases, dpo, inventory, costOfSales, dio);
    }

    private static decimal Net(Dictionary<string, (decimal debit, decimal credit)> sums, string code)
    {
        (decimal debit, decimal credit) = sums.GetValueOrDefault(code, (0m, 0m));
        return debit - credit;
    }

    private async Task<Dictionary<string, (decimal debit, decimal credit)>> SumByAccountAsync(long companyId, DateOnly? from, DateOnly to, CancellationToken ct)
    {
        var totals = await _db.Set<JournalLine>()
            .Where(l => _db.Journals.Any(j =>
                j.Id == l.JournalEntryId && j.CompanyId == companyId && j.Status == JournalStatus.Posted &&
                j.PostingDate <= to && (from == null || j.PostingDate >= from)))
            .GroupBy(l => l.AccountCode)
            .Select(g => new { Code = g.Key, Debit = g.Sum(x => x.BaseDebit), Credit = g.Sum(x => x.BaseCredit) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return totals.ToDictionary(t => t.Code, t => (t.Debit, t.Credit));
    }
}
