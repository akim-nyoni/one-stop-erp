using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Scheduling;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the reporting snapshots and quick search: aged debt, stock value and sales by day read from materialised views that stay as
/// they were until refreshed (stamped with the refresh), the scheduled refresh task, and the trigram search finding a misspelt name.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ReportingSnapshotTests
{
    private readonly AccountingFixture _fixture;

    public ReportingSnapshotTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Snapshots_hold_until_refreshed_and_search_forgives_typos()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "SNAP-C", "Mutare Cement Traders", Scenario.Usd) { PaymentTermsDays = 30 };
        Item item = new(companyId, "PC50", "Portland Cement 50kg") { CostOfSalesAccountCode = "5000", Category = "BUILD" };
        Warehouse warehouse = new(companyId, "SNAPW", "Snapshot Yard");
        db.AddRange(customer, item, warehouse);
        await db.SaveChangesAsync();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        (await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "SNAP-I1", DocumentDate = new DateOnly(2026, 1, 15),
            Currency = Scenario.Usd, NetAmount = 1_000m, SalesAccountCode = "4000" })).IsSuccess.Should().BeTrue();
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = item.Id, WarehouseId = warehouse.Id, Date = new DateOnly(2026, 1, 10), Quantity = 10m, UnitCost = 8m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        IReportingSnapshotService snapshots = scope.ServiceProvider.GetRequiredService<IReportingSnapshotService>();
        (await snapshots.AgedDebtAsync(companyId)).Rows.Should().BeEmpty("nothing is in the snapshot until it is refreshed");

        IReadOnlyList<SnapshotRefresh> refreshed = await snapshots.RefreshAsync();
        refreshed.Select(r => r.Name).Should().Equal("rpt.aged_debt", "rpt.stock_valuation", "rpt.sales_by_day");
        decimal owed = await db.OpenItems.Where(o => o.CompanyId == companyId && o.TraderId == customer.Id).SumAsync(o => o.OutstandingBase);

        Snapshot<AgedDebtSnapshotRow> aged = await snapshots.AgedDebtAsync(companyId);
        aged.RefreshedOnUtc.Should().NotBeNull();
        aged.Rows.Should().ContainSingle(r => r.CustomerCode == "SNAP-C" && r.Total == owed && r.Over90 == owed && r.Current == 0m);
        (await snapshots.StockValuationAsync(companyId)).Rows.Should().ContainSingle(r => r.WarehouseCode == "SNAPW" && r.Category == "BUILD" && r.Quantity == 10m && r.Value == 80m);
        (await snapshots.SalesByDayAsync(companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31))).Rows.Should().ContainSingle(r => r.Date == new DateOnly(2026, 1, 15) && r.Revenue == 1_000m);

        // A second invoice shows only after the next (scheduled) refresh.
        (await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "SNAP-I2", DocumentDate = new DateOnly(2026, 1, 20),
            Currency = Scenario.Usd, NetAmount = 500m, SalesAccountCode = "4000" })).IsSuccess.Should().BeTrue();
        (await snapshots.AgedDebtAsync(companyId)).Rows.Single().Total.Should().Be(owed);
        IScheduledTask task = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Key == "reporting.refresh-snapshots");
        (await task.RunAsync(companyId, DateTimeOffset.UtcNow)).Value.Should().Contain("rpt.aged_debt");
        decimal owedNow = await db.OpenItems.Where(o => o.CompanyId == companyId && o.TraderId == customer.Id).SumAsync(o => o.OutstandingBase);
        owedNow.Should().BeGreaterThan(owed);
        (await snapshots.AgedDebtAsync(companyId)).Rows.Single().Total.Should().Be(owedNow);
        (await snapshots.LastRefreshesAsync()).Should().HaveCount(3);

        // Search: a misspelling still finds the cement item and the customer; containment ranks first.
        IReadOnlyList<QuickSearchHit> hits = await snapshots.SearchAsync(companyId, "cemnt");
        hits.Should().Contain(h => h.Kind == "Item" && h.Id == item.Id).And.Contain(h => h.Kind == "Customer" && h.Id == customer.Id);
        (await snapshots.SearchAsync(companyId, "Mutare")).First().Should().Match<QuickSearchHit>(h => h.Kind == "Customer" && h.Score == 1.0);
        (await snapshots.SearchAsync(companyId, "c")).Should().BeEmpty();
    }
}
