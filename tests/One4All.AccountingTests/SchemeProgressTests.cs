using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers scheme delivery progress (brief §14 H9, H12, H13): a layout pegged then partly lodged with the Surveyor-General,
/// stages never going back; servicing certificates for all five services with progress never going backwards; a parcel
/// valued below cost and a stand without a price, read back as survey status, servicing status and the IAS 2 NRV test.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SchemeProgressTests
{
    private readonly AccountingFixture _fixture;

    public SchemeProgressTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Survey_servicing_and_valuations_drive_the_progress_reports()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        HousingScheme scheme = new(companyId, $"SCH-SP-{companyId}", "Progress Scheme", SchemeType.CouncilDelivered);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();

        IHousingService housing = scope.ServiceProvider.GetRequiredService<IHousingService>();
        string bank = $"LP-SPB-{companyId}";
        (await housing.AcquireLandAsync(companyId, bank, "Held for later", 10000m, 50000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-SPB")).IsSuccess.Should().BeTrue();
        long parcelId = (await housing.AcquireLandAsync(companyId, $"LP-SP-{companyId}", "Layout", 1200m, 12000m, new DateOnly(2026, 1, 2), "1450", "1100", "LAND-SP")).Value.LandParcelId;
        (await housing.SubdivideAsync(companyId, parcelId, scheme.Id, [new NewStand("SP-1", 400m), new NewStand("SP-2", 400m), new NewStand("SP-3", 400m)],
            ApportionMethod.Equal, new DateOnly(2026, 1, 5), "1450", "1460")).IsSuccess.Should().BeTrue();

        ISchemeProgressService progress = scope.ServiceProvider.GetRequiredService<ISchemeProgressService>();
        (await progress.RecordSurveyAsync(companyId, ["SP-1", "SP-2", "SP-9"], SurveyStage.Pegged, new DateOnly(2026, 2, 1), "SV-77")).Error.Code.Should().Be("Hse.StandNotFound");
        (await progress.RecordSurveyAsync(companyId, ["SP-1", "SP-2"], SurveyStage.Pegged, new DateOnly(2026, 2, 1), "SV-77")).Value.Should().Be(2);
        (await progress.RecordSurveyAsync(companyId, ["SP-1"], SurveyStage.DiagramLodged, new DateOnly(2026, 3, 1), "SG-1201")).Value.Should().Be(1);
        (await progress.RecordSurveyAsync(companyId, ["SP-1", "SP-2"], SurveyStage.Pegged, new DateOnly(2026, 3, 2), "SV-78")).Error.Code.Should().Be("Hse.SurveyStage");

        ServicingCertificate Certificate(BulkService service, decimal percent, int reached) => new()
        {
            CompanyId = companyId, SchemeCode = scheme.Code, Service = service, Date = new DateOnly(2026, 3, 15), PercentComplete = percent, StandsReached = reached, Reference = $"PC-{service}",
        };
        (await progress.RecordServicingAsync(Certificate(BulkService.Water, 60m, 4))).Error.Code.Should().Be("Hse.ServicingStands");
        foreach ((BulkService service, decimal percent, int reached) in new[] { (BulkService.Water, 100m, 3), (BulkService.Sewer, 80m, 2), (BulkService.Roads, 50m, 2), (BulkService.Electricity, 90m, 3) })
        {
            (await progress.RecordServicingAsync(Certificate(service, percent, reached))).IsSuccess.Should().BeTrue();
        }

        (await progress.RecordServicingAsync(Certificate(BulkService.Sewer, 70m, 2) with { Date = new DateOnly(2026, 4, 1) })).Error.Code.Should().Be("Hse.ServicingBackwards");

        (await progress.RecordValuationAsync(companyId, bank, new DateOnly(2026, 3, 31), 40000m, "Valuer & Co")).IsSuccess.Should().BeTrue();
        (await progress.RecordValuationAsync(companyId, bank, new DateOnly(2026, 3, 31), 0m, "Valuer & Co")).Error.Code.Should().Be("Hse.ValuationDetails");

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code, DateOnly asAt, decimal? costToSell = null)
        {
            Dictionary<string, object?> parameters = new() { ["AsAtDate"] = asAt };
            if (costToSell is not null)
            {
                parameters["CostToSellPercent"] = costToSell;
            }

            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("H12", new DateOnly(2026, 4, 30))).Should().Contain($"{scheme.Code},3,1,1,1,0,60");
        (await Render("H12", new DateOnly(2026, 2, 15))).Should().Contain($"{scheme.Code},3,1,2,0,0,—");

        string servicing = await Render("H13", new DateOnly(2026, 4, 30));
        servicing.Should().Contain($"{scheme.Code},100%,80%,50%,90%,—,3,0,3");
        (await progress.RecordServicingAsync(Certificate(BulkService.StormWater, 40m, 2))).IsSuccess.Should().BeTrue();
        (await Render("H13", new DateOnly(2026, 4, 30))).Should().Contain($"{scheme.Code},100%,80%,50%,90%,40%,3,2,1");

        string nrv = await Render("H9", new DateOnly(2026, 4, 30), 5m);
        nrv.Should().Contain($"{bank},Parcel,—,\"50,000.00\",Valuation 2026-03-31 (Valuer & Co),\"40,000.00\",\"2,000.00\",\"38,000.00\",12000");
        nrv.Should().Contain($"SP-1,Stand,{scheme.Code},\"4,000.00\",No price evidence,—,—,—,0");
    }
}
