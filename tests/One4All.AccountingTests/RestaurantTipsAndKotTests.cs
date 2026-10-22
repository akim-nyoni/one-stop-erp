using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantTipsAndKotTests
{
    private const long Waiter = 20301;

    private readonly AccountingFixture _fixture;

    public RestaurantTipsAndKotTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Tips_are_a_liability_and_kitchen_tickets_advance()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long menuId;
        long tableId;
        long warehouseId;
        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Core.Inventory.Warehouse wh = new(companyId, "KIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            menuId = (await res.CreateMenuItemAsync(companyId, "STEAK", "Steak", 90m, "4000", "5000", [])).Value; // no recipe
            tableId = (await res.CreateTableAsync(companyId, "T9", 2)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "ORD-9", Waiter, new DateOnly(2026, 3, 2))).Value;
            await res.AddItemAsync(companyId, orderId, menuId, 1m);
        }

        long ticketId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // Fire the order to the kitchen.
            ticketId = (await res.FireOrderAsync(companyId, orderId, "KOT-1", new DateOnly(2026, 3, 2))).Value;
            // Nothing new to fire → refused.
            (await res.FireOrderAsync(companyId, orderId, "KOT-2", new DateOnly(2026, 3, 2))).IsFailure.Should().BeTrue();

            // Advance Sent → Preparing → Ready; a backward move is refused.
            (await res.AdvanceTicketAsync(companyId, ticketId, KitchenTicketStatus.Preparing)).IsSuccess.Should().BeTrue();
            (await res.AdvanceTicketAsync(companyId, ticketId, KitchenTicketStatus.Ready)).IsSuccess.Should().BeTrue();
            (await res.AdvanceTicketAsync(companyId, ticketId, KitchenTicketStatus.Preparing)).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // Bill the steak with a 10 tip (staff money → liability).
            var bill = await res.BillOrderAsync(companyId, orderId, warehouseId, new DateOnly(2026, 3, 2), "1000", tipAmount: 10m, tipsPayableAccountCode: "2450", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId));
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            bill.Value.Tip.Should().Be(10m);
            bill.Value.Gross.Should().Be(100m); // 90 + 10 tip
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "4000" && r.Credit == 90m);   // revenue only
            tb.Rows.Should().Contain(r => r.AccountCode == "2450" && r.Credit == 10m);   // tips are a liability, not income

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            KitchenTicket ticket = await db.Set<KitchenTicket>().Include(t => t.Lines).FirstAsync(t => t.Id == ticketId);
            ticket.Status.Should().Be(KitchenTicketStatus.Ready);
            ticket.Lines.Should().ContainSingle().Which.Description.Should().Be("Steak");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // Pay the tips out to staff → clears the liability.
            (await res.PayTipsAsync(companyId, 10m, new DateOnly(2026, 3, 3), "2450", "1000")).IsSuccess.Should().BeTrue();

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().NotContain(r => r.AccountCode == "2450" && (r.Debit != 0m || r.Credit != 0m)); // tips paid out
        }
    }
}
