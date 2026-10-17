using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers what a failed unit of work leaves in the caller's change tracker: entities the caller was tracking and the
/// work did not touch stay tracked - a caller looping over items (offline sync, bank rules) can still update and save
/// them - while what the work changed is forgotten and never saved.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AtomicWorkTrackerTests
{
    private readonly AccountingFixture _fixture;

    public AtomicWorkTrackerTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_failed_unit_keeps_the_callers_untouched_entities_tracked()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long otherCustomerId;
        long orderId;
        long lineId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "AWT-C", "Held Customer", Scenario.Usd);
            Customer other = new(companyId, "AWT-O", "Bystander", Scenario.Usd);
            Item service = new(companyId, "AWT-SVC", "Consulting") { RevenueAccountCode = "4000" };
            db.AddRange(customer, other, service);
            await db.SaveChangesAsync();
            (customerId, otherCustomerId) = (customer.Id, other.Id);

            ISalesOrderService sop = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
            orderId = (await sop.CreateOrderAsync(new CreateSalesOrderRequest
            {
                CompanyId = companyId, CustomerId = customerId, OrderNumber = "AWT-1", OrderDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd,
                Lines = [new SalesOrderLineRequest(service.Id, "Consulting", 1m, 100m, null, "4000")],
            })).Value.SalesOrderId;
            lineId = await db.Set<SalesOrderLine>().Where(l => l.SalesOrderId == orderId).Select(l => l.Id).SingleAsync();
            (await sop.DespatchAsync(new DespatchRequest { CompanyId = companyId, SalesOrderId = orderId, WarehouseId = 0, Date = new DateOnly(2026, 3, 3), Lines = [new DespatchLine(lineId, 1m)] })).IsSuccess.Should().BeTrue();

            customer.OnHold = true;
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

            // The caller is tracking a customer the failing work never touches.
            Customer bystander = await db.Customers.SingleAsync(c => c.Id == otherCustomerId);

            (await scope.ServiceProvider.GetRequiredService<ISalesOrderService>().InvoiceAsync(new InvoiceOrderRequest
            {
                CompanyId = companyId, SalesOrderId = orderId, InvoiceNumber = "AWT-INV", Date = new DateOnly(2026, 3, 4),
                Lines = [new InvoiceOrderLine(lineId, 1m, 0m)],
            })).Error.Code.Should().Be("Ar.CustomerOnHold");

            // The caller can still change and save its own entity.
            db.Entry(bystander).State.Should().Be(EntityState.Unchanged);
            bystander.Name = "Bystander (updated)";
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Customers.AsNoTracking().SingleAsync(c => c.Id == otherCustomerId)).Name.Should().Be("Bystander (updated)");
            (await db.Set<SalesOrderLine>().AsNoTracking().SingleAsync(l => l.Id == lineId)).InvoicedQuantity.Should().Be(0m);
        }
    }
}
