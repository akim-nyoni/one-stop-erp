using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the stand, parcel and scheme register details and a stand's remarks history.</summary>
[Collection(AccountingCollection.Name)]
public sealed class HousingRegisterDetailsTests
{
    private readonly AccountingFixture _fixture;

    public HousingRegisterDetailsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Registers_carry_the_details_the_brief_lists()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        string parcel = $"LP-HRD-{companyId}";
        long parcelId = (await housing.AcquireLandAsync(companyId, parcel, "Parcel", 1000m, 100000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-HRD")).Value.LandParcelId;
        (await housing.SubdivideAsync(companyId, parcelId, null, [new NewStand("HRD-1", 500m), new NewStand("HRD-2", 500m)], ApportionMethod.Equal, new DateOnly(2026, 1, 5), "1450", "1460"))
            .IsSuccess.Should().BeTrue();

        IHousingRegisterDetailsService details = scope.ServiceProvider.GetRequiredService<IHousingRegisterDetailsService>();
        (await details.SetStandDetailsAsync(companyId, "hrd-1", new StandDetails("12 Msasa Rd", "W7", "Hatfield", "Z2", "Residential", "R1", "Phase 2", true, true, true, false, false))).IsSuccess.Should().BeTrue();
        (await details.SetStandDetailsAsync(companyId, "HRD-9", new StandDetails(null, null, null, null, null, null, null, false, false, false, false, false))).Error.Code.Should().Be("Hse.StandNotFound");
        HousingStand stand = await db.HousingStands.AsNoTracking().SingleAsync(s => s.CompanyId == companyId && s.StandNumber == "HRD-1");
        (stand.StreetAddress, stand.Suburb, stand.IntendedUse, stand.HasWater, stand.HasElectricity).Should().Be(("12 Msasa Rd", "Hatfield", "Residential", true, false));

        (await details.AddStandRemarkAsync(companyId, "HRD-1", "Beacons replaced after the storm.", "Surveyor Dube")).IsSuccess.Should().BeTrue();
        (await details.AddStandRemarkAsync(companyId, "HRD-1", " ", "x")).Error.Code.Should().Be("Hse.Remark");
        (await details.StandRemarksAsync(companyId, "HRD-1")).Should().ContainSingle(r => r.RecordedBy == "Surveyor Dube" && r.Text == "Beacons replaced after the storm.");

        (await details.SetParcelDetailsAsync(companyId, parcel, new ParcelDetails("Mabvuku", "W7", "Agricultural", ParcelTenure.StateLand, "Borehole", ParcelOccupation.InformallySettled)))
            .IsSuccess.Should().BeTrue();
        LandParcel land = await db.Set<LandParcel>().AsNoTracking().SingleAsync(p => p.Id == parcelId);
        (land.Tenure, land.Occupation, land.Improvements).Should().Be(((ParcelTenure?)ParcelTenure.StateLand, (ParcelOccupation?)ParcelOccupation.InformallySettled, "Borehole"));

        string scheme = $"HRD{companyId % 100000}";
        (await housing.CreateSchemeAsync(companyId, scheme, "Hatfield extension", SchemeType.CouncilDelivered)).IsSuccess.Should().BeTrue();
        (await details.SetSchemeDetailsAsync(companyId, scheme, new SchemeDetails(SchemeFunding.Grant, new DateOnly(2026, 6, 1), new DateOnly(2026, 1, 1)))).Error.Code.Should().Be("Hse.SchemeDates");
        (await details.SetSchemeDetailsAsync(companyId, scheme, new SchemeDetails(SchemeFunding.Grant, new DateOnly(2026, 1, 1), new DateOnly(2027, 6, 30)))).IsSuccess.Should().BeTrue();
    }
}
