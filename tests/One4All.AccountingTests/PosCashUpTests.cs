using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Pos;
using One4All.Core.Inventory;
using One4All.Core.Pos;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the POS cash-up close: the counted cash is reconciled against the expected drawer (float + cash
/// received − change), an over/short variance posts a balanced journal, an exact count posts none, and the
/// session closes with a Z-read either way.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PosCashUpTests
{
    private readonly AccountingFixture _fixture;

    public PosCashUpTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Cash_up_posts_the_over_short_variance_and_closes_the_till()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long terminalId;
        long warehouseId;
        long itemId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Branch branch = new(companyId, "HO", "Head Office");
            db.Branches.Add(branch);
            await db.SaveChangesAsync();
            PosTerminal terminal = new(companyId, branch.Id, "TILL1", "Front Till");
            db.PosTerminals.Add(terminal);
            Item item = new(companyId, "SODA", "Soda 500ml") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "WH1", "Shop Floor");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            terminalId = terminal.Id;
            itemId = item.Id;
            warehouseId = wh.Id;
        }

        // Overage session: float 100, one cash sale (25 in, 2 change → drawer +23), count 128 → over 5.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
            long sessionId = (await pos.OpenTillAsync(companyId, terminalId, cashierUserId: 1, openingFloat: 100m)).Value;

            await pos.CompleteSaleAsync(new CompleteSaleRequest
            {
                CompanyId = companyId, TerminalId = terminalId, TillSessionId = sessionId, CashierUserId = 1,
                SaleNumber = "S-1", SaleDate = new DateOnly(2026, 3, 2), WarehouseId = warehouseId, TaxAccountCode = "2200",
                Lines = [new PosSaleLineRequest(itemId, "Soda 500ml", 10m, 2.00m, 20m, 3m, "4000")],
                Tenders =
                [
                    new PosTenderRequest(TenderType.Cash, Scenario.Usd, 25m, "1000", IsChange: false),
                    new PosTenderRequest(TenderType.Cash, Scenario.Usd, 2m, "1000", IsChange: true),
                ],
            });

            var cashUp = await pos.CashUpAndCloseAsync(new CashUpRequest
            {
                TillSessionId = sessionId, CountedCash = 128m, Date = new DateOnly(2026, 3, 2),
                CashAccountCode = "1000", OverShortAccountCode = "7500",
            });

            cashUp.IsSuccess.Should().BeTrue(cashUp.IsFailure ? cashUp.Error.ToString() : null);
            cashUp.Value.OpeningFloat.Should().Be(100m);
            cashUp.Value.ExpectedCash.Should().Be(123m); // 100 + 25 − 2
            cashUp.Value.CountedCash.Should().Be(128m);
            cashUp.Value.Variance.Should().Be(5m);
            cashUp.Value.VarianceJournalNumber.Should().NotBeNull();
            cashUp.Value.ZReadNumber.Should().Be(1);

            // A second cash-up on the now-closed session is rejected.
            (await pos.CashUpAndCloseAsync(new CashUpRequest
            {
                TillSessionId = sessionId, CountedCash = 128m, Date = new DateOnly(2026, 3, 2),
                CashAccountCode = "1000", OverShortAccountCode = "7500",
            })).Error.Code.Should().Be("Pos.SessionClosed");
        }

        // Exact session: float 100, no sales, count 100 → no variance, no journal, Z increments to 2.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPosService pos = scope.ServiceProvider.GetRequiredService<IPosService>();
            long sessionId = (await pos.OpenTillAsync(companyId, terminalId, cashierUserId: 1, openingFloat: 100m)).Value;

            var cashUp = await pos.CashUpAndCloseAsync(new CashUpRequest
            {
                TillSessionId = sessionId, CountedCash = 100m, Date = new DateOnly(2026, 3, 2),
                CashAccountCode = "1000", OverShortAccountCode = "7500",
            });

            cashUp.Value.Variance.Should().Be(0m);
            cashUp.Value.VarianceJournalNumber.Should().BeNull();
            cashUp.Value.ZReadNumber.Should().Be(2);
        }

        // The books still balance after the variance posting.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            List<JournalLineTotals> totals = await db.Journals
                .Where(j => j.CompanyId == companyId)
                .SelectMany(j => j.Lines)
                .GroupBy(_ => 1)
                .Select(g => new JournalLineTotals(g.Sum(l => l.BaseDebit), g.Sum(l => l.BaseCredit)))
                .ToListAsync();

            totals.Single().Debit.Should().Be(totals.Single().Credit);
        }
    }

    private sealed record JournalLineTotals(decimal Debit, decimal Credit);
}
