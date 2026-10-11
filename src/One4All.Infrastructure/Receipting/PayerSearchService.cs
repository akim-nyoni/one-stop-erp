using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Receipting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Receipting;

/// <summary>The teller's live payer search across accounts, stands and students.</summary>
public sealed class PayerSearchService : IPayerSearchService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public PayerSearchService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PayerHit>> SearchAsync(long companyId, string text, int max = 20, CancellationToken ct = default)
    {
        string term = (text ?? string.Empty).Trim();
        if (term.Length < 2)
        {
            return [];
        }

        string like = $"%{term.Replace("%", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal)}%";
        string upper = term.ToUpperInvariant();
        List<(long Customer, string Matched, int Rank)> found = [];

        found.AddRange((await _db.Customers.AsNoTracking()
                .Where(c => c.CompanyId == companyId && (EF.Functions.ILike(c.Code, like) || EF.Functions.ILike(c.Name, like)))
                .OrderBy(c => c.Code).Take(max).Select(c => new { c.Id, c.Code }).ToListAsync(ct).ConfigureAwait(false))
            .Select(c => (c.Id, "Account", c.Code.ToUpperInvariant() == upper ? 0 : 2)));
        found.AddRange((await _db.Properties.AsNoTracking()
                .Where(p => p.CompanyId == companyId && EF.Functions.ILike(p.StandNumber, like))
                .OrderBy(p => p.StandNumber).Take(max).Select(p => new { p.RatepayerCustomerId, p.StandNumber }).ToListAsync(ct).ConfigureAwait(false))
            .Select(p => (p.RatepayerCustomerId, $"Rates on stand {p.StandNumber}", p.StandNumber.ToUpperInvariant() == upper ? 0 : 1)));
        found.AddRange((await _db.Students.AsNoTracking()
                .Where(s => s.CompanyId == companyId && s.IsEnrolled && (EF.Functions.ILike(s.AdmissionNumber, like) || EF.Functions.ILike(s.FullName, like)))
                .OrderBy(s => s.AdmissionNumber).Take(max).Select(s => new { Payer = s.SponsorCustomerId ?? s.GuardianCustomerId, s.AdmissionNumber, s.FullName }).ToListAsync(ct).ConfigureAwait(false))
            .Select(s => (s.Payer, $"Fees for {s.FullName} ({s.AdmissionNumber})", s.AdmissionNumber.ToUpperInvariant() == upper ? 0 : 1)));

        List<long> ids = [.. found.Select(f => f.Customer).Distinct()];
        Dictionary<long, (string Code, string Name)> customers = await _db.Customers.AsNoTracking().Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => (c.Code, c.Name), ct).ConfigureAwait(false);
        Dictionary<long, decimal> balances = await _db.OpenItems.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && ids.Contains(i.TraderId) && i.OutstandingBase != 0m)
            .GroupBy(i => i.TraderId)
            .Select(g => new { g.Key, Balance = g.Sum(i => i.Sign == LedgerSign.Debit ? i.OutstandingBase : -i.OutstandingBase) })
            .ToDictionaryAsync(b => b.Key, b => b.Balance, ct).ConfigureAwait(false);

        return [.. found.Where(f => customers.ContainsKey(f.Customer))
            .OrderBy(f => f.Rank).ThenBy(f => customers[f.Customer].Code, StringComparer.Ordinal)
            .DistinctBy(f => (f.Customer, f.Matched)).Take(max)
            .Select(f => new PayerHit(f.Customer, customers[f.Customer].Code, customers[f.Customer].Name, f.Matched, balances.GetValueOrDefault(f.Customer)))];
    }
}
