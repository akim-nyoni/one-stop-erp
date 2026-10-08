using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Inventory;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers specific identification and usage variance: a serial-costed item's issue costs the units that actually leave (named, or the earliest
/// batches), and a standard-costed assembly posts components used beyond standard to the usage variance account, its price variance apart.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SpecificCostAndUsageVarianceTests
{
    private readonly AccountingFixture _fixture;

    public SpecificCostAndUsageVarianceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Specific_units_carry_their_own_cost()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item generator = new(companyId, "SI-GEN", "Generator", costingMethod: CostingMethod.SpecificIdentification) { Tracking = ItemTracking.Serial, CostOfSalesAccountCode = "5000" };
        Warehouse store = new(companyId, "SI-WH", "Store");
        db.AddRange(generator, store);
        await db.SaveChangesAsync();

        IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        foreach ((string serial, decimal cost, int day) in new[] { ("GEN-001", 1_000m, 1), ("GEN-002", 1_400m, 2) })
        {
            (await inventory.ReceiveAsync(new ReceiveStockRequest
            {
                CompanyId = companyId, ItemId = generator.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 3, day), Quantity = 1m, UnitCost = cost, CreditAccountCode = "1100",
                Lots = [new LotQuantity(serial, 1m)],
            })).IsSuccess.Should().BeTrue();
        }

        // The named unit costs 1,400 (the average would be 1,200); the next, unnamed, is the one left at 1,000.
        IssueStockRequest Issue(params LotQuantity[] lots) => new()
        {
            CompanyId = companyId, ItemId = generator.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 3, 10), Quantity = 1m, Reference = "SI-OUT", Lots = lots,
        };
        (await inventory.IssueAsync(Issue(new LotQuantity("GEN-002", 1m)))).Value.Value.Should().Be(1_400m);
        (await inventory.IssueAsync(Issue())).Value.Value.Should().Be(1_000m);
        StockBalance balance = await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == generator.Id);
        (balance.QuantityOnHand, balance.TotalValue).Should().Be((0m, 0m));
    }

    [SkippableFact]
    public async Task A_standard_costed_assembly_separates_usage_from_price_variance()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.AddRange(new LedgerAccount(companyId, "5110", "Purchase Price Variance", AccountType.Expense), new LedgerAccount(companyId, "5120", "Usage Variance", AccountType.Expense));
        Item flour = new(companyId, "UV-FLOUR", "Flour");
        Item loaf = new(companyId, "UV-LOAF", "Loaf", costingMethod: CostingMethod.Standard) { StandardCost = 10m, PriceVarianceAccountCode = "5110", UsageVarianceAccountCode = "5120" };
        Warehouse bakery = new(companyId, "UV-WH", "Bakery");
        db.AddRange(flour, loaf, bakery);
        await db.SaveChangesAsync();
        BillOfMaterials bom = new(companyId, "loaf-1", loaf.Id, 1m);
        bom.AddLine(flour.Id, 4m);
        db.BillsOfMaterials.Add(bom);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = flour.Id, WarehouseId = bakery.Id, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 2m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        // 10 loaves should take 40 flour; 45 were used: 5 × 2 = 10 usage variance. The loaves come in at the standard quantities' 80 against a
        // standard of 100, so the price variance is a 20 credit.
        AssemblyResult result = (await scope.ServiceProvider.GetRequiredService<IAssemblyService>().AssembleAsync(new AssemblyRequest
        {
            CompanyId = companyId, BillOfMaterialsId = bom.Id, WarehouseId = bakery.Id, Date = new DateOnly(2026, 3, 5), PlannedOutput = 10m, ActualOutput = 10m,
            Usage = [new ComponentUsage(flour.Id, 45m)], ClearingAccountCode = "2600",
        })).Value;
        result.TotalCost.Should().Be(90m);
        (await db.AssemblyRuns.AsNoTracking().SingleAsync(r => r.Id == result.RunId)).UsageVariance.Should().Be(10m);

        async Task<decimal> Net(string code) => await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).Where(l => l.AccountCode == code)
            .SumAsync(l => l.BaseDebit - l.BaseCredit);
        (await Net("5120"), await Net("5110"), await Net("2600")).Should().Be((10m, -20m, 0m));
        (await db.StockBalances.AsNoTracking().SingleAsync(b => b.ItemId == loaf.Id)).TotalValue.Should().Be(100m);
    }
}
