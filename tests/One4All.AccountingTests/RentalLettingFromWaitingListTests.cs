using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Housing;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Where the council lets only from the rental waiting list, a unit goes to an active rental applicant (who is then housed), never to someone off
/// the list or on another list, and a household already renting a council unit cannot take a second.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RentalLettingFromWaitingListTests
{
    private readonly AccountingFixture _fixture;

    public RentalLettingFromWaitingListTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Rental_units_are_let_from_the_rental_waiting_list_one_per_household()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer tenant = new(companyId, "RW-T", "Rental Tenant", Scenario.Usd) { NationalIdNumber = $"RW-ID-{companyId}" };
        db.Customers.Add(tenant);
        HousingApplicant renter = new(companyId, $"RW-ID-{companyId}", "Rental Tenant", new DateOnly(2020, 1, 1));
        renter.SetDetails(HousingCategory.Rental, new DateOnly(1990, 1, 1), new DateOnly(2010, 1, 1), 1, false, 300m, CurrentTenure.Lodger);
        renter.Register($"RW-R-{companyId}", renter.RegistrationDate);
        HousingApplicant buyer = new(companyId, $"RW-ID2-{companyId}", "Stand Seeker", new DateOnly(2020, 1, 1));
        buyer.SetDetails(HousingCategory.HighDensity, new DateOnly(1990, 1, 1), new DateOnly(2010, 1, 1), 1, false, 300m, CurrentTenure.Lodger);
        buyer.Register($"RW-B-{companyId}", buyer.RegistrationDate);
        db.HousingApplicants.AddRange(renter, buyer);
        await db.SaveChangesAsync();

        IRentalHousingService rentals = scope.ServiceProvider.GetRequiredService<IRentalHousingService>();
        foreach (string unit in new[] { "RW-U1", "RW-U2" })
        {
            (await rentals.AddUnitAsync(companyId, unit, "Flat", RentalUnitType.Flat, 2, 150m, "4400", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        }

        (await scope.ServiceProvider.GetRequiredService<IWaitingListService>().SetQualificationRulesAsync(companyId,
            new QualificationRules(false, false, false, new Dictionary<HousingCategory, decimal>(), RentalFromWaitingList: true))).IsSuccess.Should().BeTrue();
        LetUnitRequest Let(string unit, string? applicant) => new()
        {
            CompanyId = companyId, UnitCode = unit, TenantCustomerId = tenant.Id, StartDate = new DateOnly(2026, 2, 1), WaitingListNumber = applicant,
        };

        (await rentals.LetAsync(Let("RW-U1", null))).Error.Code.Should().Be("Hse.LetFromWaitingList");
        (await rentals.LetAsync(Let("RW-U1", buyer.WaitingListNumber))).Error.Code.Should().Be("Hse.ApplicantNotEligible");
        (await rentals.LetAsync(Let("RW-U1", renter.WaitingListNumber))).IsSuccess.Should().BeTrue();
        (await db.HousingApplicants.AsNoTracking().SingleAsync(a => a.Id == renter.Id)).Status.Should().Be(ApplicantStatus.Allocated);
        (await rentals.LetAsync(Let("RW-U2", renter.WaitingListNumber))).Error.Code.Should().Be("Hse.ApplicantNotEligible", "the applicant has been housed");

        (await scope.ServiceProvider.GetRequiredService<IWaitingListService>().SetQualificationRulesAsync(companyId, QualificationRules.None)).IsSuccess.Should().BeTrue();
        (await rentals.LetAsync(Let("RW-U2", null))).Error.Code.Should().Be("Hse.HouseholdTenancy");
    }
}
