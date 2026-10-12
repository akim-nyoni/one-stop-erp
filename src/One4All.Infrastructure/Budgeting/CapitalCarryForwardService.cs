using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Budgeting;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Budgeting;

/// <summary>Proposes and approves the carry-forward of unspent capital into the next year's capital budget.</summary>
public sealed class CapitalCarryForwardService : ICapitalCarryForwardService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public CapitalCarryForwardService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<CapitalUnspent>>> ProposeAsync(long companyId, long fiscalYearId, DateOnly asAt, CancellationToken ct = default)
    {
        var year = await _db.FiscalYears.AsNoTracking().Where(y => y.CompanyId == companyId && y.Id == fiscalYearId).Select(y => new { y.StartDate }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (year is null)
        {
            return Error.NotFound("Bud.YearNotFound", $"Fiscal year {fiscalYearId} not found.");
        }

        var budgeted = await (from l in _db.Set<BudgetLine>().AsNoTracking()
                              join b in _db.Set<Budget>().AsNoTracking() on l.BudgetId equals b.Id
                              where b.CompanyId == companyId && b.FiscalYearId == fiscalYearId && b.Type == BudgetType.Capital
                                    && (b.Status == BudgetStatus.Approved || b.Status == BudgetStatus.Active)
                              group l by l.AccountCode into g
                              select new { Account = g.Key, Amount = g.Sum(l => l.Amount) }).ToListAsync(ct).ConfigureAwait(false);
        List<string> accounts = [.. budgeted.Select(x => x.Account)];
        var spent = await (from l in _db.Set<JournalLine>().AsNoTracking()
                           join j in _db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                           where j.CompanyId == companyId && (j.Status == JournalStatus.Posted || j.Status == JournalStatus.Reversed) && j.PostingDate >= year.StartDate
                                 && j.PostingDate <= asAt && accounts.Contains(l.AccountCode)
                           group l by l.AccountCode into g
                           select new { Account = g.Key, Net = g.Sum(l => l.BaseDebit - l.BaseCredit) }).ToDictionaryAsync(x => x.Account, x => x.Net, ct).ConfigureAwait(false);
        Dictionary<string, decimal> carried = await _db.Set<CapitalCarryForward>().AsNoTracking().Where(c => c.CompanyId == companyId && c.FromFiscalYearId == fiscalYearId)
            .GroupBy(c => c.AccountCode).Select(g => new { g.Key, Amount = g.Sum(c => c.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Amount, ct).ConfigureAwait(false);
        return Result<IReadOnlyList<CapitalUnspent>>.Success([.. budgeted.OrderBy(x => x.Account, StringComparer.Ordinal)
            .Select(x => new CapitalUnspent(x.Account, x.Amount, Math.Max(0m, spent.GetValueOrDefault(x.Account)), carried.GetValueOrDefault(x.Account)))]);
    }

    /// <inheritdoc />
    public Task<Result<decimal>> ApproveAsync(long companyId, long fiscalYearId, long toBudgetId, IReadOnlyList<CarryForwardAmount> amounts, string reference, long approvedByUserId, DateOnly asAt,
        CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(amounts);
            if (string.IsNullOrWhiteSpace(reference) || amounts.Count == 0 || amounts.Any(a => a.Amount <= 0m))
            {
                return Error.Validation("Bud.CarryForward", "A carry-forward needs its approval reference and positive amounts per vote.");
            }

            Budget? target = await _db.Set<Budget>().Include(b => b.Lines).FirstOrDefaultAsync(b => b.CompanyId == companyId && b.Id == toBudgetId, ct).ConfigureAwait(false);
            DateOnly? fromStart = await _db.FiscalYears.Where(y => y.Id == fiscalYearId && y.CompanyId == companyId).Select(y => (DateOnly?)y.StartDate).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            DateOnly? toStart = target is null ? null
                : await _db.FiscalYears.Where(y => y.Id == target.FiscalYearId).Select(y => (DateOnly?)y.StartDate).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (target is null || target.Type != BudgetType.Capital || fromStart is null || toStart is null || toStart <= fromStart)
            {
                return Error.Rule("Bud.CarryTarget", "Unspent capital carries into a capital budget of a later year.");
            }

            Result<IReadOnlyList<CapitalUnspent>> proposal = await ProposeAsync(companyId, fiscalYearId, asAt, ct).ConfigureAwait(false);
            if (proposal.IsFailure)
            {
                return proposal.Error;
            }

            decimal total = 0m;
            foreach (CarryForwardAmount amount in amounts)
            {
                string account = amount.AccountCode.Trim().ToUpperInvariant();
                CapitalUnspent? vote = proposal.Value.FirstOrDefault(v => string.Equals(v.AccountCode, account, StringComparison.OrdinalIgnoreCase));
                if (vote is null || amount.Amount > vote.Carryable)
                {
                    return Error.Rule("Bud.CarryTooMuch", $"Vote {account} has {vote?.Carryable ?? 0m:N2} unspent capital to carry, not {amount.Amount:N2}.");
                }

                CapitalCarryForward act = new(companyId, fiscalYearId, target.Id, vote.AccountCode, amount.Amount, reference.Trim(), approvedByUserId, asAt);
                _db.Set<CapitalCarryForward>().Add(act);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                target.AddLine(vote.AccountCode, 1, amount.Amount, carryForwardId: act.Id);
                total += amount.Amount;
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<decimal>.Success(total);
        }, ct);
}
