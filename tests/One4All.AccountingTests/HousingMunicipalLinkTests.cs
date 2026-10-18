using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Municipal;
using One4All.Core.Housing;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers housing → municipality: the council links stands on sale; selling a stand creates the billable property with the
/// buyer as owner and ratepayer and the configured service starting on the sale date, so the first month's charge is
/// pro-rata; a stand already rated to someone else is left alone and reported as such (the ratepayer moves by transfer with clearance); other events do nothing while the trigger is the sale.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class HousingMunicipalLinkTests
{
    private readonly AccountingFixture _fixture;

    public HousingMunicipalLinkTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_sold_stand_becomes_a_billable_property_charged_pro_rata()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer buyer = new(companyId, "HM-BUY", "Linked Buyer", Scenario.Usd);
        Customer other = new(companyId, "HM-OTH", "Existing Ratepayer", Scenario.Usd);
        db.Customers.AddRange(buyer, other);
        Tariff refuse = new(companyId, "REF-HM", "Refuse", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4600") { FixedCharge = 100m };
        Tariff refuseToo = new(companyId, "REF-HM2", "Refuse (alt)", ServiceType.Refuse, TariffType.Flat, new DateOnly(2026, 1, 1), "4600") { FixedCharge = 120m };
        db.Tariffs.AddRange(refuse, refuseToo);
        LandParcel parcel = new(companyId, $"LP-HM-{companyId}", "Linked parcel", 5000m, 50000m);
        db.LandParcels.Add(parcel);
        await db.SaveChangesAsync();
        HousingStand stand = new(companyId, parcel.Id, null, $"HM-1-{companyId}", 400m, 3000m) { Ward = "Ward 4" };
        HousingStand rated = new(companyId, parcel.Id, null, $"HM-2-{companyId}", 400m, 3000m);
        db.HousingStands.AddRange(stand, rated);
        db.Properties.Add(new Property(companyId, rated.StandNumber, "Ward 1", other.Id));
        await db.SaveChangesAsync();

        IHousingMunicipalLinkService link = scope.ServiceProvider.GetRequiredService<IHousingMunicipalLinkService>();
        (await link.ConfigureAsync(companyId, MunicipalLinkTrigger.Sale, "Ward 9", [refuse.Id, refuseToo.Id])).Error.Code.Should().Be("Hse.MunicipalLink");
        (await link.ConfigureAsync(companyId, MunicipalLinkTrigger.Sale, "Ward 9", [999_999_999])).Error.Code.Should().Be("Hse.TariffNotFound");
        (await link.ConfigureAsync(companyId, MunicipalLinkTrigger.Sale, "Ward 9", [refuse.Id])).IsSuccess.Should().BeTrue();

        // Sold on 16 April: the property, its owner and its refuse service all start that day.
        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        DateOnly sold = new(2026, 4, 16);
        (await housing.SellStandAsync(companyId, stand.Id, buyer.Id, buyer.Name, 8000m, sold, "4100", "1460", "5100", $"HM-INV-{companyId}")).IsSuccess.Should().BeTrue();
        Property property = await db.Properties.AsNoTracking().Include(p => p.Services).SingleAsync(p => p.CompanyId == companyId && p.StandNumber == stand.StandNumber);
        (property.Ward, property.RatepayerCustomerId, property.Valuation, property.SizeSquareMetres).Should().Be(("Ward 4", buyer.Id, 8000m, 400m), "the stand's own ward, not the link's default");
        property.Services.Should().ContainSingle().Which.StartDate.Should().Be(sold);
        IReadOnlyList<OwnerEntry> owners = await scope.ServiceProvider.GetRequiredService<IOwnershipService>().OwnersAsync(companyId, property.Id, sold);
        owners.Should().ContainSingle().Which.SharePercent.Should().Be(100m);

        // April's refuse run charges half a month: 15 of 30 days.
        (await scope.ServiceProvider.GetRequiredService<IMunicipalBillingService>().RunFixedChargeBillingAsync(companyId, ServiceType.Refuse, new DateOnly(2026, 4, 30), "HMR")).IsSuccess.Should().BeTrue();
        (await db.OpenItems.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentNumber == $"HMR-{property.Id}-202604")).OriginalBase.Should().Be(50m);

        // A stand already on the roll is left to the transfer process; other events do nothing under a sale trigger.
        (await housing.SellStandAsync(companyId, rated.Id, buyer.Id, buyer.Name, 8000m, sold, "4100", "1460", "5100", $"HM-INV2-{companyId}")).IsSuccess.Should().BeTrue();
        (await db.Properties.AsNoTracking().SingleAsync(p => p.CompanyId == companyId && p.StandNumber == rated.StandNumber)).RatepayerCustomerId.Should().Be(other.Id);
        (await link.OnEventAsync(companyId, stand.Id, buyer.Id, MunicipalLinkTrigger.TitleRegistered, sold, "T 1/2026")).Value.Outcome.Should().Be(MunicipalLinkOutcome.NotTriggered);
        (await link.OnEventAsync(companyId, rated.Id, buyer.Id, MunicipalLinkTrigger.Sale, sold, null)).Value.Should().Be(new MunicipalLinkResult(MunicipalLinkOutcome.RatepayerDiffers, (await db.Properties.AsNoTracking().SingleAsync(p => p.StandNumber == rated.StandNumber && p.CompanyId == companyId)).Id));
    }
}
