using Microsoft.EntityFrameworkCore;
using One4All.Core.Accounting;
using One4All.Core.System;

namespace One4All.Infrastructure.Persistence.Seeding;

/// <summary>
/// Idempotently adds the currencies every installation starts with: the US dollar, Zimbabwe Gold and the South African rand. A currency already
/// set up (by code) is left as it is, so changes made on the currency screen are kept.
/// </summary>
public sealed class CurrencySeeder
{
    /// <summary>The seeded currencies: code, name, symbol.</summary>
    public static readonly IReadOnlyList<(string Code, string Name, string Symbol)> Defaults =
    [
        ("USD", "US Dollar", "$"),
        ("ZWG", "Zimbabwe Gold", "ZiG"),
        ("ZAR", "South African Rand", "R"),
    ];

    private readonly One4AllDbContext _db;

    /// <summary>Creates the seeder.</summary>
    /// <param name="db">The database context.</param>
    public CurrencySeeder(One4AllDbContext db) => _db = db;

    /// <summary>Adds any default currency not yet set up.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of currencies added.</returns>
    public async Task<int> SeedAsync(CancellationToken ct = default)
    {
        List<CurrencyCode> codes = [.. Defaults.Select(d => CurrencyCode.Parse(d.Code))];
        HashSet<CurrencyCode> existing = [.. await _db.Currencies.IgnoreQueryFilters().Where(c => codes.Contains(c.Code)).Select(c => c.Code).ToListAsync(ct).ConfigureAwait(false)];
        int added = 0;
        foreach ((string code, string name, string symbol) in Defaults)
        {
            if (existing.Add(CurrencyCode.Parse(code)))
            {
                _db.Currencies.Add(new Currency(CurrencyCode.Parse(code), name, symbol));
                added++;
            }
        }

        if (added > 0)
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return added;
    }
}
