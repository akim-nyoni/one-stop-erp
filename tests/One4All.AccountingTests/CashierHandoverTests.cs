using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers handing a till to the next cashier: the outgoing session is cashed up and closed, and the incoming one opens with the drawer as float.</summary>
[Collection(AccountingCollection.Name)]
public sealed class CashierHandoverTests
{
    private readonly AccountingFixture _fixture;

    public CashierHandoverTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_till_passes_to_the_next_cashier_with_the_counted_drawer()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "1001", "Till Cash", AccountType.Asset));
        db.Accounts.Add(new LedgerAccount(companyId, "7501", "Cash Over/Short", AccountType.Expense));
        Branch branch = new(companyId, "HO-BR", "Hand-over Branch");
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        PosTerminal terminal = new(companyId, branch.Id, "HO-T1", "Till 1");
        db.PosTerminals.Add(terminal);
        await db.SaveChangesAsync();

        IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
        long morning = (await pos.OpenTillAsync(companyId, terminal.Id, cashierUserId: 5, openingFloat: 50m)).Value;
        CashierHandoverRequest Handover(long incoming, decimal counted) => new()
        {
            TillSessionId = morning, IncomingCashierUserId = incoming, CountedCash = counted, CountedByUserId = 5, Date = new DateOnly(2026, 4, 1), CashAccountCode = "1001", OverShortAccountCode = "7501",
        };
        (await pos.HandOverAsync(Handover(5, 50m))).Error.Code.Should().Be("Pos.SameCashier");
        CashierHandover handed = (await pos.HandOverAsync(Handover(6, 48m))).Value;
        (handed.OutgoingCashUp.ExpectedCash, handed.OutgoingCashUp.CountedCash, handed.OutgoingCashUp.Variance).Should().Be((50m, 48m, -2m), "the morning cashier is 2 short");

        List<TillSession> sessions = await db.TillSessions.AsNoTracking().Where(s => s.TerminalId == terminal.Id).OrderBy(s => s.Id).ToListAsync();
        sessions.Select(s => (s.CashierUserId, s.Status)).Should().Equal((5L, TillSessionStatus.Closed), (6L, TillSessionStatus.Open));
        sessions[1].OpeningFloat.Should().Be(48m, "the afternoon cashier starts with what was counted");
    }
}
