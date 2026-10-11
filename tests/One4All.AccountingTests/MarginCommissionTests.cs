using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers commission on gross margin: the rep earns on revenue invoiced less the cost of the goods despatched.</summary>
[Collection(AccountingCollection.Name)]
public sealed class MarginCommissionTests
{
    private readonly AccountingFixture _fixture;

    public MarginCommissionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_margin_rep_earns_on_revenue_less_cost_of_sales()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        SalesRep rep = new(companyId, "REP-MGN", "Earns on margin", CommissionBasis.Margin, 10m);
        Item chair = new(companyId, "MC-CHAIR", "Chair") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
        Warehouse store = new(companyId, "MC-WH", "Store");
        db.AddRange(rep, chair, store);
        await db.SaveChangesAsync();
        Customer buyer = new(companyId, "MC-C", "Margin Customer", Scenario.Usd) { SalesRepId = rep.Id };
        db.Customers.Add(buyer);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = chair.Id, WarehouseId = store.Id, Date = new DateOnly(2026, 3, 1), Quantity = 10m, UnitCost = 60m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        // Four chairs at 100 cost 240: revenue 400, margin 160, commission 16.
        ISalesOrderService orders = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        long order = (await orders.CreateOrderAsync(new CreateSalesOrderRequest
        {
            CompanyId = companyId, CustomerId = buyer.Id, OrderNumber = "MC-1", OrderDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd,
            Lines = [new SalesOrderLineRequest(chair.Id, "Chair", 4m, 100m, null, "4000")],
        })).Value.SalesOrderId;
        long line = await db.Set<SalesOrderLine>().Where(l => l.SalesOrderId == order).Select(l => l.Id).SingleAsync();
        (await orders.DespatchAsync(new DespatchRequest
        {
            CompanyId = companyId, SalesOrderId = order, WarehouseId = store.Id, Date = new DateOnly(2026, 3, 3), Reference = "MC-D1", Lines = [new DespatchLine(line, 4m)],
        })).IsSuccess.Should().BeTrue();
        (await orders.InvoiceAsync(new InvoiceOrderRequest
        {
            CompanyId = companyId, SalesOrderId = order, InvoiceNumber = "MC-INV-1", Date = new DateOnly(2026, 3, 4), Lines = [new InvoiceOrderLine(line, 4m, 0m)],
        })).IsSuccess.Should().BeTrue();

        CommissionStatement statement = (await scope.ServiceProvider.GetRequiredService<ICommissionService>().StatementAsync(companyId, rep.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31))).Value;
        statement.Lines.Should().ContainSingle().Which.Should().Be(new CommissionLine(buyer.Id, "MC-C", 160m, 16m));
    }
}
