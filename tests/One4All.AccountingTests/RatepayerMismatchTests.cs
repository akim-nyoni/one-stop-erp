using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Housing;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Thirteenth audit #14: a stand held by one customer and billed to another is listed on H98 until a transfer with clearance moves the ratepayer.</summary>
[Collection(AccountingCollection.Name)]
public sealed class RatepayerMismatchTests
{
    private readonly AccountingFixture _fixture;

    public RatepayerMismatchTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_stand_billed_to_someone_other_than_its_holder_is_listed()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Customer buyer = new(companyId, "RPM-B", "New Holder", Scenario.Usd), seller = new(companyId, "RPM-S", "Old Ratepayer", Scenario.Usd);
        LandParcel parcel = new(companyId, $"LP-RPM-{companyId}", "Mismatch parcel", 5000m, 50000m);
        db.AddRange(buyer, seller, parcel);
        await db.SaveChangesAsync();
        HousingStand sold = new(companyId, parcel.Id, null, $"RPM-1-{companyId}", 300m, 1000m), kept = new(companyId, parcel.Id, null, $"RPM-2-{companyId}", 300m, 1000m);
        db.HousingStands.AddRange(sold, kept);
        await db.SaveChangesAsync();
        sold.Sell(buyer.Id, buyer.Name, 3000m, new DateOnly(2026, 2, 1));
        kept.Sell(seller.Id, seller.Name, 3000m, new DateOnly(2026, 2, 1));
        db.Properties.AddRange(new Property(companyId, sold.StandNumber, "Ward 1", seller.Id), new Property(companyId, kept.StandNumber, "Ward 1", seller.Id));
        await db.SaveChangesAsync();

        var rendered = await scope.ServiceProvider.GetRequiredService<IReportRunner>().RenderAsync(companyId, "H98", new Dictionary<string, object?>(), ReportFormat.Csv);
        rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
        string csv = Encoding.UTF8.GetString(rendered.Value.Content);
        csv.Should().Contain($"{sold.StandNumber},RPM-B New Holder,RPM-S Old Ratepayer,-,Lodge a transfer with a rates clearance certificate").And.NotContain(kept.StandNumber);
    }
}
