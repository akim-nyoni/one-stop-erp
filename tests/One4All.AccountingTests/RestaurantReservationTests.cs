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
public sealed class RestaurantReservationTests
{
    private const long Waiter = 20501;

    private readonly AccountingFixture _fixture;

    public RestaurantReservationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Reservations_book_seat_and_guard_against_clashes_and_oversize_parties()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long tableId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            tableId = (await res.CreateTableAsync(companyId, "R1", 4)).Value;
        }

        long reservationId;
        DateTime slot = new(2026, 3, 10, 19, 0, 0, DateTimeKind.Utc);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();

            // A party larger than the table seats is refused.
            (await res.ReserveTableAsync(companyId, tableId, "Big Group", 6, slot)).IsFailure.Should().BeTrue();

            // Book a party of 4 for 19:00.
            var booked = await res.ReserveTableAsync(companyId, tableId, "Ada Lovelace", 4, slot, "555-0101", "Window seat");
            booked.IsSuccess.Should().BeTrue(booked.IsFailure ? booked.Error.ToString() : null);
            reservationId = booked.Value;

            // A second booking on the same table 30 minutes later clashes (within the 90-minute turn time).
            (await res.ReserveTableAsync(companyId, tableId, "Clash", 2, slot.AddMinutes(30))).IsFailure.Should().BeTrue();

            // Two hours later is clear.
            (await res.ReserveTableAsync(companyId, tableId, "Later Guest", 2, slot.AddHours(2))).IsSuccess.Should().BeTrue();
        }

        // The reservations report lists the upcoming bookings.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string resv = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-RESV", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            resv.Should().Contain("Ada Lovelace").And.Contain("Later Guest").And.Contain("R1");
            resv.Should().NotContain("Clash"); // the clashing booking was never created
        }

        // Seat the guest → an order opens on the table and the reservation is marked seated.
        long orderId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            var seated = await res.SeatReservationAsync(companyId, reservationId, "RES-ORD-1", Waiter, new DateOnly(2026, 3, 10));
            seated.IsSuccess.Should().BeTrue(seated.IsFailure ? seated.Error.ToString() : null);
            orderId = seated.Value;

            // Seating it again is refused (it is no longer booked).
            (await res.SeatReservationAsync(companyId, reservationId, "RES-ORD-2", Waiter, new DateOnly(2026, 3, 10))).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            TableReservation reservation = await db.Set<TableReservation>().FirstAsync(r => r.Id == reservationId);
            reservation.Status.Should().Be(ReservationStatus.Seated);
            reservation.SeatedOrderId.Should().Be(orderId);

            RestaurantTable table = await db.Set<RestaurantTable>().FirstAsync(t => t.Id == tableId);
            table.Status.Should().Be(TableStatus.Occupied); // seating opened the order

            // The now-seated reservation drops off the upcoming list.
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string resv = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "RES-RESV", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            resv.Should().NotContain("Ada Lovelace").And.Contain("Later Guest");
        }
    }

    [SkippableFact]
    public async Task Reservation_cancellation_and_no_show_need_a_booked_reservation()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long tableId;
        long cancelId;
        long noShowId;
        DateTime slot = new(2026, 3, 12, 20, 0, 0, DateTimeKind.Utc);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            tableId = (await res.CreateTableAsync(companyId, "R2", 2)).Value;
            cancelId = (await res.ReserveTableAsync(companyId, tableId, "To Cancel", 2, slot)).Value;
            noShowId = (await res.ReserveTableAsync(companyId, tableId, "No Show", 2, slot.AddHours(3))).Value;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();

            // Cancellation needs a reason.
            (await res.CancelReservationAsync(companyId, cancelId, "  ")).IsFailure.Should().BeTrue();
            (await res.CancelReservationAsync(companyId, cancelId, "Guest called to cancel")).IsSuccess.Should().BeTrue();
            // A cancelled reservation cannot be cancelled again or marked a no-show.
            (await res.CancelReservationAsync(companyId, cancelId, "again")).IsFailure.Should().BeTrue();

            (await res.MarkReservationNoShowAsync(companyId, noShowId)).IsSuccess.Should().BeTrue();
            (await res.MarkReservationNoShowAsync(companyId, noShowId)).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<TableReservation>().FirstAsync(r => r.Id == cancelId)).Status.Should().Be(ReservationStatus.Cancelled);
            (await db.Set<TableReservation>().FirstAsync(r => r.Id == noShowId)).Status.Should().Be(ReservationStatus.NoShow);

            // Since the earlier slot is now free, re-booking it at the same time is allowed.
            IRestaurantService res = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
            (await res.ReserveTableAsync(companyId, tableId, "Rebooked", 2, slot)).IsSuccess.Should().BeTrue();
        }
    }
}
