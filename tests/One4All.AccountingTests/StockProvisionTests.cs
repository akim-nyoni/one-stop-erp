using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Inventory;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers stock provisions end to end: at 31 August a widget that will sell below cost is written down to NRV (19), a
/// gadget unsold since January is slow-moving (25) and recent stock needs nothing; posting books 44, a repeat posts
/// nothing, and once the gadget has sold the September run releases its 25.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class StockProvisionAccountingTests
{
    private readonly AccountingFixture _fixture;

    public StockProvisionAccountingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Provisions_are_booked_adjusted_and_released()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long gadgetId;
        long warehouseId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            db.Accounts.Add(new LedgerAccount(companyId, "1490", "Stock Provision", AccountType.Asset));
            db.Accounts.Add(new LedgerAccount(companyId, "5190", "Stock Write-downs", AccountType.Expense));
            Item widget = new(companyId, "SP-WIDGET", "Widget") { CostOfSalesAccountCode = "5000" };
            Item gadget = new(companyId, "SP-GADGET", "Gadget") { CostOfSalesAccountCode = "5000" };
            Item gizmo = new(companyId, "SP-GIZMO", "Gizmo") { CostOfSalesAccountCode = "5000" };
            Warehouse wh = new(companyId, "SPWH", "Main");
            db.AddRange(widget, gadget, gizmo, wh);
            await db.SaveChangesAsync();
            (gadgetId, warehouseId) = (gadget.Id, wh.Id);

            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await Receive(inv, companyId, widget.Id, wh.Id, new DateOnly(2026, 8, 1), 12m, 10m);
            (await inv.IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = widget.Id, WarehouseId = wh.Id, Date = new DateOnly(2026, 8, 20), Quantity = 2m })).IsSuccess.Should().BeTrue();
            await Receive(inv, companyId, gadget.Id, wh.Id, new DateOnly(2026, 1, 2), 5m, 20m);
            await Receive(inv, companyId, gizmo.Id, wh.Id, new DateOnly(2026, 8, 10), 4m, 25m);

            IPricingService pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();
            long list = (await pricing.CreatePriceListAsync(companyId, "SP-DEF", "Default", Scenario.Usd, isDefault: true)).Value;
            await pricing.SetPriceAsync(companyId, list, widget.Id, 1m, 9m);   // 10 × 9 × 0.9 = 81 < cost 100
            await pricing.SetPriceAsync(companyId, list, gadget.Id, 1m, 30m);  // NRV 135 > cost 100, but unsold 241 days
            await pricing.SetPriceAsync(companyId, list, gizmo.Id, 1m, 40m);
        }

        DateOnly august = new(2026, 8, 31);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IStockProvisionService provisions = scope.ServiceProvider.GetRequiredService<IStockProvisionService>();
            StockProvisionReport report = (await provisions.AssessAsync(companyId, august, StockProvisionPolicy.Default, "1490")).Value;
            report.Lines.Single(l => l.ItemCode == "SP-WIDGET").Should().BeEquivalentTo(new { Quantity = 10m, Cost = 100m, NetRealisableValue = 81m, Provision = 19m, Basis = ProvisionBasis.NetRealisableValue });
            report.Lines.Single(l => l.ItemCode == "SP-GADGET").Should().BeEquivalentTo(new { DaysSinceLastIssue = 241, Provision = 25m, Basis = ProvisionBasis.SlowMoving });
            report.Lines.Single(l => l.ItemCode == "SP-GIZMO").Provision.Should().Be(0m);
            report.TargetProvision.Should().Be(44m);
            report.BookedProvision.Should().Be(0m);

            StockProvisionPostingResult posted = (await provisions.PostAsync(companyId, august, StockProvisionPolicy.Default, "1490", "5190")).Value;
            posted.JournalNumber.Should().NotBeNull();
            (await provisions.PostAsync(companyId, august, StockProvisionPolicy.Default, "1490", "5190")).Value.JournalNumber.Should().BeNull();

            // The gadgets sell in September: their provision is released.
            (await scope.ServiceProvider.GetRequiredService<IInventoryService>().IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = gadgetId, WarehouseId = warehouseId, Date = new DateOnly(2026, 9, 10), Quantity = 5m })).IsSuccess.Should().BeTrue();
            StockProvisionPostingResult september = (await provisions.PostAsync(companyId, new DateOnly(2026, 9, 30), StockProvisionPolicy.Default, "1490", "5190")).Value;
            september.Report.TargetProvision.Should().Be(19m);
            september.Report.Adjustment.Should().Be(-25m);

            ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 9, 30))).Value;
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Single(r => r.AccountCode == "1490").Credit.Should().Be(19m);
            tb.Rows.Single(r => r.AccountCode == "5190").Debit.Should().Be(19m);
        }
    }

    private static async Task Receive(IInventoryService inv, long companyId, long itemId, long warehouseId, DateOnly date, decimal quantity, decimal unitCost)
        => (await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = date, Quantity = quantity, UnitCost = unitCost, CreditAccountCode = "1100" })).IsSuccess.Should().BeTrue();
}
