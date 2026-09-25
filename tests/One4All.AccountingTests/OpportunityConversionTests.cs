using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Crm;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Crm;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class OpportunityConversionTests
{
    private readonly AccountingFixture _fixture;

    public OpportunityConversionTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Converting_a_won_opportunity_raises_a_sales_order_and_marks_it_won()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long itemId;
        long linkedOppId;
        long orphanOppId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "OPP-C", "Prospect Ltd", Scenario.Usd);
            db.Customers.Add(customer);
            Item item = new(companyId, "OPP-WIDGET", "Widget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            itemId = item.Id;

            Opportunity linked = new(companyId, "Big deal", "Jane Buyer", 5_000m, customerId);
            linked.MoveTo(OpportunityStage.Negotiation);
            Opportunity orphan = new(companyId, "No customer yet", "Anon", 1_000m); // no CustomerId
            db.Set<Opportunity>().AddRange(linked, orphan);
            await db.SaveChangesAsync();
            linkedOppId = linked.Id;
            orphanOppId = orphan.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IOpportunityService opps = scope.ServiceProvider.GetRequiredService<IOpportunityService>();

            // An opportunity with no linked customer cannot be converted.
            (await opps.ConvertToSalesOrderAsync(companyId, orphanOppId, "SO-OPP-X", new DateOnly(2026, 5, 1), Scenario.Usd,
                [new SalesOrderLineRequest(itemId, "Widget", 10m, 50m, null, "4000")])).IsFailure.Should().BeTrue();

            // Convert the linked opportunity → a sales order.
            var convert = await opps.ConvertToSalesOrderAsync(companyId, linkedOppId, "SO-OPP-1", new DateOnly(2026, 5, 1), Scenario.Usd,
                [new SalesOrderLineRequest(itemId, "Widget", 10m, 50m, null, "4000")]);
            convert.IsSuccess.Should().BeTrue(convert.IsFailure ? convert.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<Opportunity>().FirstAsync(o => o.Id == linkedOppId)).Stage.Should().Be(OpportunityStage.Won);
            (await db.Set<SalesOrder>().AnyAsync(o => o.CompanyId == companyId && o.OrderNumber == "SO-OPP-1")).Should().BeTrue();
        }
    }
}
