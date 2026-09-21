using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RestaurantDeliveryTests
{
    private const long Waiter = 21501;

    private readonly AccountingFixture _fixture;

    public RestaurantDeliveryTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Delivery_orders_assign_a_driver_and_advance_through_dispatch_states()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long deliveryId;
        long takeawayId;
        long driverUserId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Core.Security.User driver = new("driver1", "Dan the Driver");
            db.Set<Core.Security.User>().Add(driver);
            await db.SaveChangesAsync();
            driverUserId = driver.Id;

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            deliveryId = (await res.OpenOffPremiseOrderAsync(companyId, "DL-1", OrderType.Delivery, Waiter, new DateOnly(2026, 4, 1), "Grace", "555-1", "12 Baker St")).Value;
            takeawayId = (await res.OpenOffPremiseOrderAsync(companyId, "TK-1", OrderType.Takeaway, Waiter, new DateOnly(2026, 4, 1), "Ada")).Value;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<TableOrder>().FirstAsync(o => o.Id == deliveryId)).DeliveryStatus.Should().Be(DeliveryStatus.Pending);

            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            // A takeaway order has no dispatch state.
            (await res.AssignDeliveryDriverAsync(companyId, takeawayId, driverUserId)).IsFailure.Should().BeTrue();
            (await res.AdvanceDeliveryAsync(companyId, takeawayId, DeliveryStatus.OutForDelivery)).IsFailure.Should().BeTrue();

            // Assign a driver → Assigned; you cannot go backwards to Pending.
            (await res.AssignDeliveryDriverAsync(companyId, deliveryId, driverUserId)).IsSuccess.Should().BeTrue();
            (await res.AdvanceDeliveryAsync(companyId, deliveryId, DeliveryStatus.Pending)).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            TableOrder delivery = await db.Set<TableOrder>().FirstAsync(o => o.Id == deliveryId);
            delivery.DeliveryStatus.Should().Be(DeliveryStatus.Assigned);
            delivery.DriverUserId.Should().Be(driverUserId);

            // Active deliveries report lists it with driver and address.
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string deliv = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-DELIV", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            deliv.Should().Contain("DL-1").And.Contain("Grace").And.Contain("12 Baker St").And.Contain("Dan the Driver");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            (await res.AdvanceDeliveryAsync(companyId, deliveryId, DeliveryStatus.OutForDelivery)).IsSuccess.Should().BeTrue();
            (await res.AdvanceDeliveryAsync(companyId, deliveryId, DeliveryStatus.Delivered)).IsSuccess.Should().BeTrue();
            // Once delivered, no further changes.
            (await res.AdvanceDeliveryAsync(companyId, deliveryId, DeliveryStatus.Failed)).IsFailure.Should().BeTrue();
            (await res.AssignDeliveryDriverAsync(companyId, deliveryId, driverUserId)).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            // A delivered order drops off the active-deliveries list.
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string deliv = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-DELIV", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            deliv.Should().NotContain("DL-1");
        }
    }
}
