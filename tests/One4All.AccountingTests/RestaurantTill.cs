using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.AccountingTests;

/// <summary>An open till session for restaurant billing tests: the cash of a bill is taken into an open till session.</summary>
public static class RestaurantTill
{
    /// <summary>The cashier of the restaurant test till (a high id no created user shares).</summary>
    public const long CashierUserId = 900_701;

    /// <summary>Returns the company's open restaurant test till session, opening the till (branch, terminal, session) the first time.</summary>
    /// <param name="services">The scope's services.</param>
    /// <param name="companyId">The company.</param>
    /// <returns>The open till session id.</returns>
    public static async Task<long> SessionAsync(IServiceProvider services, long companyId)
    {
        One4AllDbContext db = services.GetRequiredService<One4AllDbContext>();
        long? open = await db.TillSessions.Join(db.PosTerminals, s => s.TerminalId, t => t.Id, (s, t) => new { s, t })
            .Where(x => x.s.CompanyId == companyId && x.t.Code == "RES-TILL" && x.s.Status == TillSessionStatus.Open)
            .Select(x => (long?)x.s.Id).FirstOrDefaultAsync();
        if (open is long id)
        {
            return id;
        }

        Branch branch = new(companyId, "RTB", "Restaurant Till Branch");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "RES-TILL", "Restaurant Till") { Profile = TerminalProfile.Restaurant };
        db.PosTerminals.Add(terminal);
        await db.SaveChangesAsync();
        TillSession session = new(companyId, terminal.Id, CashierUserId, DateTimeOffset.UtcNow, 0m);
        db.TillSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }
}
