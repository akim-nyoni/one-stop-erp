using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers facility hire: the town hall at 200 a day is booked 10–12 May (600 invoiced); a booking sharing the 12th is
/// refused while 13–14 May is taken (400); the calendar shows both; cancelling the second with a 50% refund credits 200
/// and frees the days for another booking; a cancelled booking cannot be cancelled again. Hire income nets to 1,000.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FacilityBookingTests
{
    private readonly AccountingFixture _fixture;

    public FacilityBookingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Facilities_are_booked_without_clashes_and_cancelled_with_refunds()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4945", "Facility Hire", AccountType.Income));
        Customer choir = new(companyId, "FB-CHOIR", "Town Choir", Scenario.Usd);
        Customer club = new(companyId, "FB-CLUB", "Chess Club", Scenario.Usd);
        db.Customers.AddRange(choir, club);
        await db.SaveChangesAsync();

        IFacilityBookingService facilities = scope.ServiceProvider.GetRequiredService<IFacilityBookingService>();
        (await facilities.CreateFacilityAsync(companyId, "hall", "Town Hall", 200m, "4945")).IsSuccess.Should().BeTrue();
        (await facilities.CreateFacilityAsync(companyId, "HALL", "Again", 100m, "4945")).Error.Code.Should().Be("Mun.FacilityExists");

        FacilityBookingRequest Book(Customer who, int from, int to) => new()
        {
            CompanyId = companyId, FacilityCode = "HALL", CustomerId = who.Id, FromDate = new DateOnly(2026, 5, from), ToDate = new DateOnly(2026, 5, to), Purpose = "Event", BookedOn = new DateOnly(2026, 4, 1),
        };

        FacilityBooked concert = (await facilities.BookAsync(Book(choir, 10, 12))).Value;
        concert.Should().Be(new FacilityBooked(concert.BookingNumber, 3, 600m));
        (await facilities.BookAsync(Book(club, 12, 14))).Error.Code.Should().Be("Mun.FacilityTaken");
        FacilityBooked tournament = (await facilities.BookAsync(Book(club, 13, 14))).Value;
        tournament.Fee.Should().Be(400m);
        (await facilities.BookAsync(Book(club, 15, 14))).Error.Code.Should().Be("Mun.BookingDetails");

        (await facilities.CalendarAsync(companyId, "hall", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31))).Value
            .Select(e => (e.CustomerCode, e.FromDate.Day, e.ToDate.Day)).Should().Equal(("FB-CHOIR", 10, 12), ("FB-CLUB", 13, 14));

        (await facilities.CancelAsync(companyId, tournament.BookingNumber, new DateOnly(2026, 4, 20), 50m)).Value.Should().Be(200m);
        (await facilities.CancelAsync(companyId, tournament.BookingNumber, new DateOnly(2026, 4, 20), 50m)).Error.Code.Should().Be("Mun.BookingCancelled");
        (await facilities.BookAsync(Book(choir, 13, 13))).Value.Fee.Should().Be(200m);

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 4, 30))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "4945").Credit.Should().Be(1000m);
    }
}
