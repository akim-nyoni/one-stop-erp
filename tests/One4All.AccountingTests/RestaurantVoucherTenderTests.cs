using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Restaurant;
using One4All.Application.Abstractions.Vouchers;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantVoucherTenderTests
{
    private const long Waiter = 21701;

    private readonly AccountingFixture _fixture;

    public RestaurantVoucherTenderTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Paying_a_bill_with_a_gift_card_debits_the_liability_and_reduces_the_balance()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long warehouseId;
        long menuId;
        long bigOrderId;
        long smallOrderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Core.Inventory.Warehouse wh = new(companyId, "VTKIT", "Kitchen");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            warehouseId = wh.Id;

            // Issue a $100 gift card (Dr cash / Cr gift-card liability 2300).
            IVoucherService vouchers = scope.ServiceProvider.GetRequiredService<IVoucherService>();
            (await vouchers.IssueGiftCardAsync(new IssueGiftCardRequest
            {
                CompanyId = companyId,
                CardNumber = "GC-100",
                Currency = Scenario.Usd,
                Value = 100m,
                IssueDate = new DateOnly(2026, 4, 5),
                CashAccountCode = "1000",
                LiabilityAccountCode = "2300",
            })).IsSuccess.Should().BeTrue();

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            menuId = (await res.CreateMenuItemAsync(companyId, "VT-MEAL", "Meal", 60m, "4000", "5000", [])).Value;
            long t1 = (await res.CreateTableAsync(companyId, "VT1", 4)).Value;
            long t2 = (await res.CreateTableAsync(companyId, "VT2", 4)).Value;
            bigOrderId = (await res.OpenOrderAsync(companyId, t1, "VT-ORD-1", Waiter, new DateOnly(2026, 4, 5))).Value;
            await res.AddItemAsync(companyId, bigOrderId, menuId, 1m); // 60
            smallOrderId = (await res.OpenOrderAsync(companyId, t2, "VT-ORD-2", Waiter, new DateOnly(2026, 4, 5))).Value;
            await res.AddItemAsync(companyId, smallOrderId, menuId, 1m); // 60, but card will only have 40 left
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // Pay the first 60 bill with the gift card.
            var bill = await res.BillToVoucherAsync(companyId, bigOrderId, "GC-100", warehouseId, new DateOnly(2026, 4, 5), "2300");
            bill.IsSuccess.Should().BeTrue(bill.IsFailure ? bill.Error.ToString() : null);
            bill.Value.Gross.Should().Be(60m);

            IVoucherService vouchers = scope.ServiceProvider.GetRequiredService<IVoucherService>();
            (await vouchers.GetBalanceAsync(companyId, "GC-100")).Value.Balance.Should().Be(40m); // 100 - 60

            // The second 60 bill exceeds the remaining 40 → refused, and the order stays open.
            (await res.BillToVoucherAsync(companyId, smallOrderId, "GC-100", warehouseId, new DateOnly(2026, 4, 5), "2300")).IsFailure.Should().BeTrue();
            (await res.BillToVoucherAsync(companyId, smallOrderId, "GC-100", warehouseId, new DateOnly(2026, 4, 5), "2300")).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<Core.Restaurant.TableOrder>().FindAsync(smallOrderId))!.Status.Should().Be(Core.Restaurant.TableOrderStatus.Open);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "4000" && r.Credit == 60m);  // revenue from the paid bill
            // Gift-card liability: 100 issued (Cr) less 60 redeemed (Dr) = 40 remaining credit.
            tb.Rows.Should().Contain(r => r.AccountCode == "2300" && r.Credit == 40m);
        }
    }
}
