using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers automatic order numbering: a blank order number takes the next SO- / PO- number from the
/// company's sequence (gapless, in order), while an explicit number is kept verbatim.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class OrderNumberingTests
{
    private readonly AccountingFixture _fixture;

    public OrderNumberingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Blank_order_numbers_are_allocated_from_the_sequence_and_explicit_ones_kept()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long supplierId;
        long itemId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "NUM-C", "Buyer", Scenario.Usd);
            db.Customers.Add(customer);
            Supplier supplier = new(companyId, "NUM-S", "Supplier", Scenario.Usd);
            db.Suppliers.Add(supplier);
            Item item = new(companyId, "NUM-ITEM", "Thing") { RevenueAccountCode = "4000", CostOfSalesAccountCode = "5000" };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            supplierId = supplier.Id;
            itemId = item.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
            IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
            DateOnly date = new(2026, 7, 1);

            var so1 = await sop.CreateOrderAsync(new CreateSalesOrderRequest { CompanyId = companyId, CustomerId = customerId, OrderNumber = "", OrderDate = date, Currency = Scenario.Usd, Lines = [new SalesOrderLineRequest(itemId, "Thing", 1m, 5m, null, "4000")] });
            so1.IsSuccess.Should().BeTrue(so1.IsFailure ? so1.Error.ToString() : null);
            var so2 = await sop.CreateOrderAsync(new CreateSalesOrderRequest { CompanyId = companyId, CustomerId = customerId, OrderNumber = "   ", OrderDate = date, Currency = Scenario.Usd, Lines = [new SalesOrderLineRequest(itemId, "Thing", 1m, 5m, null, "4000")] });
            var soKeyed = await sop.CreateOrderAsync(new CreateSalesOrderRequest { CompanyId = companyId, CustomerId = customerId, OrderNumber = "MY-SO-7", OrderDate = date, Currency = Scenario.Usd, Lines = [new SalesOrderLineRequest(itemId, "Thing", 1m, 5m, null, "4000")] });

            so1.Value.OrderNumber.Should().StartWith("SO-").And.EndWith("00001");
            so2.Value.OrderNumber.Should().StartWith("SO-").And.EndWith("00002");
            soKeyed.Value.OrderNumber.Should().Be("MY-SO-7");

            var po1 = await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest { CompanyId = companyId, SupplierId = supplierId, OrderNumber = "", OrderDate = date, Currency = Scenario.Usd, Lines = [new PurchaseOrderLineRequest(itemId, "Thing", 1m, 2m)] });
            po1.IsSuccess.Should().BeTrue(po1.IsFailure ? po1.Error.ToString() : null);
            var po2 = await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest { CompanyId = companyId, SupplierId = supplierId, OrderNumber = "", OrderDate = date, Currency = Scenario.Usd, Lines = [new PurchaseOrderLineRequest(itemId, "Thing", 1m, 2m)] });
            var poKeyed = await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest { CompanyId = companyId, SupplierId = supplierId, OrderNumber = "MY-PO-9", OrderDate = date, Currency = Scenario.Usd, Lines = [new PurchaseOrderLineRequest(itemId, "Thing", 1m, 2m)] });

            po1.Value.OrderNumber.Should().StartWith("PO-").And.EndWith("00001");
            po2.Value.OrderNumber.Should().StartWith("PO-").And.EndWith("00002");
            poKeyed.Value.OrderNumber.Should().Be("MY-PO-9");

            // The two sequences are independent of each other.
            so1.Value.OrderNumber.Should().NotBe(po1.Value.OrderNumber);
        }
    }
}
