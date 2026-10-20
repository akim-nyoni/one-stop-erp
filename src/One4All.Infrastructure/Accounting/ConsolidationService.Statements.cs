using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Infrastructure.Accounting;

/// <summary>The group statements: consolidated, with intercompany, investment and non-controlling interest eliminations (IFRS 10).</summary>
public sealed partial class ConsolidationService
{
    /// <inheritdoc />
    public async Task<Result<GroupStatements>> GroupStatementsAsync(GroupStatementsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        long[] ids = [.. request.CompanyIds.Distinct()];
        if (!ids.Contains(request.ParentCompanyId))
        {
            return Error.Validation("Consolidation.Parent", "The parent must be one of the group's companies.");
        }

        Result<ConsolidatedTrialBalance> group = await ConsolidatedTrialBalanceAsync(ids, request.AsAtDate, request.IntercompanyBalanceAccounts, ct).ConfigureAwait(false);
        if (group.IsFailure)
        {
            return group.Error;
        }

        Dictionary<string, decimal> nets = group.Value.Rows.ToDictionary(r => r.AccountCode, r => r.Debit - r.Credit, StringComparer.Ordinal);
        Dictionary<string, string> names = group.Value.Rows.ToDictionary(r => r.AccountCode, r => r.AccountName, StringComparer.Ordinal);
        using IDisposable unscoped = _db.WithoutCompanyScope();
        Dictionary<string, AccountType> types = await _db.Accounts.AsNoTracking().Where(a => ids.Contains(a.CompanyId)).GroupBy(a => a.AccountCode)
            .Select(g => new { Code = g.Key, Type = g.Min(a => a.Type) }).ToDictionaryAsync(x => x.Code, x => x.Type, StringComparer.Ordinal, ct).ConfigureAwait(false);

        // Intercompany sales and purchases: eliminated to the extent they match, pro rata over the accounts.
        decimal sales = -request.IntercompanyRevenueAccounts.Sum(c => nets.GetValueOrDefault(c));
        decimal purchases = request.IntercompanyExpenseAccounts.Sum(c => nets.GetValueOrDefault(c));
        decimal trading = Math.Max(0m, Math.Min(sales, purchases));
        if (trading > 0m)
        {
            Reduce(nets, request.IntercompanyRevenueAccounts, trading, sales);
            Reduce(nets, request.IntercompanyExpenseAccounts, trading, purchases);
        }

        // Each subsidiary: the investment is set against the group's share of its share capital (any excess is goodwill); the outside
        // shareholders take their share of its net assets and profit.
        decimal investment = request.InvestmentAccountCode is string invest ? await NetAsync([request.ParentCompanyId], [invest], request.AsAtDate, ct).ConfigureAwait(false) : 0m;
        decimal capitalAcquired = 0m;
        decimal nci = 0m;
        decimal nciProfit = 0m;
        decimal nciReserves = 0m;
        foreach (long subsidiary in ids.Where(i => i != request.ParentCompanyId))
        {
            decimal owned = request.Ownership.TryGetValue(subsidiary, out decimal percent) ? Math.Clamp(percent, 0m, 100m) / 100m : 1m;
            Result<ConsolidatedTrialBalance> own = await ConsolidatedTrialBalanceAsync([subsidiary], request.AsAtDate, ct).ConfigureAwait(false);
            if (own.IsFailure)
            {
                return own.Error;
            }

            Dictionary<string, decimal> subNets = own.Value.Rows.ToDictionary(r => r.AccountCode, r => r.Debit - r.Credit, StringComparer.Ordinal);
            decimal capital = -request.SubsidiaryShareCapitalAccounts.Sum(c => subNets.GetValueOrDefault(c));
            decimal netAssets = subNets.Where(n => types.GetValueOrDefault(n.Key) is AccountType.Asset or AccountType.Liability).Sum(n => n.Value);
            decimal profit = -subNets.Where(n => types.GetValueOrDefault(n.Key) is AccountType.Income or AccountType.Expense).Sum(n => n.Value);
            capitalAcquired += Round(owned * capital);
            nci += Round((1m - owned) * netAssets);
            nciProfit += Round((1m - owned) * profit);
            nciReserves += Round((1m - owned) * (netAssets - capital - profit));
        }

        if (request.InvestmentAccountCode is string investmentCode && investment != 0m)
        {
            nets[investmentCode] = nets.GetValueOrDefault(investmentCode) - investment;
        }

        foreach (string code in request.SubsidiaryShareCapitalAccounts)
        {
            if (ids.Length > 1)
            {
                nets[code] = nets.GetValueOrDefault(code) + await SubsidiaryCapitalAsync(ids, request.ParentCompanyId, code, request.AsAtDate, ct).ConfigureAwait(false);
            }
        }

        decimal goodwill = Math.Max(0m, investment - capitalAcquired);
        List<GroupStatementLine> income = [], expenses = [], assets = [], liabilities = [], equity = [];
        foreach ((string code, decimal net) in nets.OrderBy(n => n.Key, StringComparer.Ordinal))
        {
            if (net == 0m)
            {
                continue;
            }

            GroupStatementLine Line(decimal amount) => new(code, names.GetValueOrDefault(code, code), amount);
            switch (types.GetValueOrDefault(code, AccountType.Asset))
            {
                case AccountType.Income: income.Add(Line(-net)); break;
                case AccountType.Expense: expenses.Add(Line(net)); break;
                case AccountType.Liability: liabilities.Add(Line(-net)); break;
                case AccountType.Equity: equity.Add(Line(-net)); break;
                default: assets.Add(Line(net)); break;
            }
        }

        if (goodwill > 0m)
        {
            assets.Add(new GroupStatementLine("GOODWILL", "Goodwill", goodwill));
        }

        // A bargain purchase (the share of capital over the investment) is a gain in equity; the outside shareholders' share of the
        // subsidiaries' reserves leaves the owners' equity for the non-controlling interest.
        if (capitalAcquired > investment && investment > 0m)
        {
            equity.Add(new GroupStatementLine("BARGAIN", "Gain on bargain purchase", capitalAcquired - investment));
        }

        if (nciReserves != 0m)
        {
            equity.Add(new GroupStatementLine("NCI-RES", "Non-controlling interest's share of reserves", -nciReserves));
        }

        return new GroupStatements(request.AsAtDate, income, expenses, assets, liabilities, equity, goodwill, nci, nciProfit, trading, group.Value.IntercompanyEliminated);
    }

    private static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    // Takes an elimination off a set of accounts in proportion to their balances (each shrinks toward nil by the same fraction).
    private static void Reduce(Dictionary<string, decimal> nets, IReadOnlyCollection<string> codes, decimal elimination, decimal magnitude)
    {
        foreach (string code in codes.Where(nets.ContainsKey).ToList())
        {
            nets[code] -= Round(nets[code] * elimination / magnitude);
        }
    }

    private async Task<decimal> NetAsync(long[] companyIds, string[] codes, DateOnly asAt, CancellationToken ct)
        => await _db.Set<JournalLine>().Where(l => codes.Contains(l.AccountCode)
                && _db.Journals.Any(j => j.Id == l.JournalEntryId && companyIds.Contains(j.CompanyId) && j.Status == JournalStatus.Posted && j.PostingDate <= asAt))
            .SumAsync(l => l.BaseDebit - l.BaseCredit, ct).ConfigureAwait(false);

    // The subsidiaries' share capital on an account (a credit, returned positive) - the parent's own capital stays.
    private async Task<decimal> SubsidiaryCapitalAsync(long[] ids, long parent, string code, DateOnly asAt, CancellationToken ct)
        => -await NetAsync([.. ids.Where(i => i != parent)], [code], asAt, ct).ConfigureAwait(false);
}
