using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantSplitTenderTests
{
    private const long Waiter = 22001;

    private readonly AccountingFixture _fixture;

    public RestaurantSplitTenderTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_bill_can_be_settled_across_several_tenders_that_must_sum_to_the_gross()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long menuId;
        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Core.Inventory.Warehouse wh = new(companyId, "SPKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            menuId = (await res.CreateMenuItemAsync(companyId, "SP-FEAST", "Feast", 100m, "4000", "5000", [])).Value;
            long tableId = (await res.CreateTableAsync(companyId, "SP1", 6)).Value;
            orderId = (await res.OpenOrderAsync(companyId, tableId, "SP-ORD-1", Waiter, new DateOnly(2026, 4, 9))).Value;
            await res.AddItemAsync(companyId, orderId, menuId, 1m, taxAmount: 15m); // net 100 + VAT 15 = gross 115
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();

            // Tenders that do not sum to the gross are refused (order stays open).
            (await res.BillWithTendersAsync(companyId, orderId, warehouseId, new DateOnly(2026, 4, 9),
                [new TenderLine("1000", 50m), new TenderLine("1100", 50m)], "2200", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId))).IsFailure.Should().BeTrue();

            // 70 cash + 45 bank = 115.
            var bill = await res.BillWithTendersAsync(companyId, orderId, warehouseId, new DateOnly(2026, 4, 9),
                [new TenderLine("1000", 70m), new TenderLine("1100", 45m)], "2200", tillSessionId: await RestaurantTill.SessionAsync(scope.ServiceProvider, companyId));
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            bill.Value.Gross.Should().Be(115m);
            bill.Value.FullyBilled.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "1000" && r.Debit == 70m);  // cash tender
            tb.Rows.Should().Contain(r => r.AccountCode == "1100" && r.Debit == 45m);  // bank tender
            tb.Rows.Should().Contain(r => r.AccountCode == "4000" && r.Credit == 100m); // revenue
            tb.Rows.Should().Contain(r => r.AccountCode == "2200" && r.Credit == 15m);  // VAT
        }
    }
}
