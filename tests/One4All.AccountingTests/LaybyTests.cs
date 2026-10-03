using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers lay-byes: a 500 television needs a 20% deposit, so 50 is refused and 100 accepted; the set is held out
/// of free stock; instalments of 300 and 100 complete it, making the sale (revenue 500, cost 200) and issuing it, while an
/// overpayment is refused. A second plan with a 10% forfeiture defaults after its final date: 20 is kept and 180 owed
/// back. A third is cancelled with nothing forfeited. Deposits held net to nil and the ledger agrees with check data.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LaybyTests
{
    private readonly AccountingFixture _fixture;

    public LaybyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Laybys_hold_payments_until_completion_and_forfeit_on_default()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "2488", "Lay-bye Deposits", AccountType.Liability));
        db.Accounts.Add(new LedgerAccount(companyId, "2487", "Refunds Payable", AccountType.Liability));
        db.Accounts.Add(new LedgerAccount(companyId, "4990", "Forfeited Deposits", AccountType.Income));
        Item tv = new(companyId, "LB-TV", "Television") { CostOfSalesAccountCode = "5000" };
        Warehouse shop = new(companyId, "LBSH", "Shop");
        Customer customer = new(companyId, "LB-C", "Lay-bye Customer", Scenario.Usd);
        db.AddRange(tv, shop, customer);
        await db.SaveChangesAsync();

        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        (await inventory.ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = tv.Id, WarehouseId = shop.Id, Date = new DateOnly(2026, 3, 1), Quantity = 3m, UnitCost = 200m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        ILaybyService laybys = scope.ServiceProvider.GetRequiredService<ILaybyService>();
        LaybyRequest Plan(string number, decimal deposit, decimal forfeit) => new()
        {
            CompanyId = companyId, CustomerId = customer.Id, PlanNumber = number, WarehouseId = shop.Id, OpenedOn = new DateOnly(2026, 3, 2), FinalDate = new DateOnly(2026, 4, 30),
            Deposit = deposit, MinimumDepositPercent = 20m, ForfeitPercent = forfeit, BankAccountCode = "1100", DepositAccountCode = "2488", RevenueAccountCode = "4000",
            ForfeitureAccountCode = "4990", Lines = [new LaybyLineRequest(tv.Id, 1m, 500m)],
        };

        (await laybys.OpenAsync(Plan("LB-1", 50m, 10m))).Error.Code.Should().Be("Layby.Deposit");
        long first = (await laybys.OpenAsync(Plan("LB-1", 100m, 10m))).Value;
        IStockAvailabilityService availability = scope.ServiceProvider.GetRequiredService<IStockAvailabilityService>();
        (await availability.AvailabilityAsync(companyId, tv.Id)).Value.Free.Should().Be(2m);

        (await laybys.PayAsync(companyId, first, new DateOnly(2026, 3, 15), 300m, "1100")).Value.Should().Be(new LaybyPayment(400m, 100m, false));
        (await laybys.PayAsync(companyId, first, new DateOnly(2026, 3, 20), 150m, "1100")).Error.Code.Should().Be("Layby.Payment");
        (await laybys.PayAsync(companyId, first, new DateOnly(2026, 3, 31), 100m, "1100")).Value.Completed.Should().BeTrue();
        StockAvailability afterSale = (await availability.AvailabilityAsync(companyId, tv.Id)).Value;
        (afterSale.OnHand, afterSale.Free).Should().Be((2m, 2m));

        long second = (await laybys.OpenAsync(Plan("LB-2", 100m, 10m))).Value;
        (await laybys.PayAsync(companyId, second, new DateOnly(2026, 4, 10), 100m, "1100")).IsSuccess.Should().BeTrue();
        long third = (await laybys.OpenAsync(Plan("LB-3", 150m, 0m))).Value;
        (await laybys.CancelAsync(companyId, third, new DateOnly(2026, 4, 12), "1100")).Value.Should().Be(new LaybyCancellation("LB-3", 150m, 0m, 150m));

        IReadOnlyList<LaybyCancellation> defaulted = (await laybys.DefaultOverdueAsync(companyId, new DateOnly(2026, 5, 5), "2487")).Value;
        defaulted.Should().Equal(new LaybyCancellation("LB-2", 200m, 20m, 180m));
        (await laybys.PayAsync(companyId, second, new DateOnly(2026, 5, 6), 10m, "1100")).Error.Code.Should().Be("Layby.NotOpen");
        (await availability.AvailabilityAsync(companyId, tv.Id)).Value.Free.Should().Be(2m);

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 5, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Should().NotContain(r => r.AccountCode == "2488" && (r.Debit != 0m || r.Credit != 0m));
        tb.Rows.Single(r => r.AccountCode == "4000").Credit.Should().Be(500m);
        tb.Rows.Single(r => r.AccountCode == "5000").Debit.Should().Be(200m);
        tb.Rows.Single(r => r.AccountCode == "4990").Credit.Should().Be(20m);
        tb.Rows.Single(r => r.AccountCode == "2487").Credit.Should().Be(180m);
        (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value.Issues.Should().BeEmpty();
    }
}
