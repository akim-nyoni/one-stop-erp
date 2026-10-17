using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.JobCosting;
using One4All.Core.Housing;
using One4All.Core.JobCosting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers servicing cost per stand: a servicing contract linked to a scheme has its actual cost capitalised into the
/// scheme's unsold stands by area, and a second contract by frontage once frontages are recorded; each run moves only the
/// cost added since the last; a sold stand takes none; and the cost per stand shows land and servicing.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ServicingCostTests
{
    private readonly AccountingFixture _fixture;

    public ServicingCostTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Servicing_cost_is_apportioned_to_unsold_stands_once()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer client = new(companyId, "SV-CON", "Council works", Scenario.Usd);
        db.Customers.Add(client);
        LandParcel parcel = new(companyId, $"LP-SV-{companyId}", "Servicing parcel", 5000m, 50000m);
        HousingScheme scheme = new(companyId, $"SCH-SV-{companyId}", "Servicing Scheme", SchemeType.CouncilDelivered);
        db.LandParcels.Add(parcel);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();
        HousingStand small = new(companyId, parcel.Id, scheme.Id, "SV-1", 300m, 1000m);
        HousingStand large = new(companyId, parcel.Id, scheme.Id, "SV-2", 600m, 2000m);
        HousingStand sold = new(companyId, parcel.Id, scheme.Id, "SV-3", 300m, 1000m);
        db.HousingStands.AddRange(small, large, sold);
        await db.SaveChangesAsync();
        sold.Sell(client.Id, client.Name, 5000m, new DateOnly(2026, 1, 20));
        await db.SaveChangesAsync();

        IJobCostingService jobs = scope.ServiceProvider.GetRequiredService<IJobCostingService>();
        long roads = (await jobs.CreateContractAsync(companyId, $"SVC-R-{companyId}", "Roads", client.Id, 100_000m, 90_000m, 0m, "4300")).Value;
        long water = (await jobs.CreateContractAsync(companyId, $"SVC-W-{companyId}", "Water reticulation", client.Id, 100_000m, 90_000m, 0m, "4300")).Value;
        (await jobs.CaptureCostAsync(companyId, roads, "Earthworks", JobCostSource.Subcontractor, 9000m, new DateOnly(2026, 3, 15), "RC-1")).IsSuccess.Should().BeTrue();

        IServicingCostService servicing = scope.ServiceProvider.GetRequiredService<IServicingCostService>();
        (await servicing.ApportionAsync(companyId, scheme.Id, new DateOnly(2026, 3, 31), "1450", "1460")).Error.Code.Should().Be("Hse.NoServicingContract");
        (await servicing.LinkAsync(companyId, scheme.Id, roads, ServicingBasis.ByArea)).IsSuccess.Should().BeTrue();
        (await servicing.LinkAsync(companyId, scheme.Id, roads, ServicingBasis.Equal)).Error.Code.Should().Be("Hse.ContractLinked");

        // March: 9,000 of roads by area — 3,000 and 6,000; the sold stand takes none.
        IReadOnlyList<ServicingRunLine> march = (await servicing.ApportionAsync(companyId, scheme.Id, new DateOnly(2026, 3, 31), "1450", "1460")).Value;
        march.Should().ContainSingle().Which.Apportioned.Should().Be(9000m);
        (await servicing.ApportionAsync(companyId, scheme.Id, new DateOnly(2026, 3, 31), "1450", "1460")).Value.Single().Apportioned.Should().Be(0m);

        // The water contract goes by frontage, which has to be recorded first.
        (await servicing.LinkAsync(companyId, scheme.Id, water, ServicingBasis.ByFrontage)).IsSuccess.Should().BeTrue();
        (await jobs.CaptureCostAsync(companyId, water, "Pipes", JobCostSource.PurchaseInvoice, 3500m, new DateOnly(2026, 4, 10), "WP-1")).IsSuccess.Should().BeTrue();
        (await jobs.CaptureCostAsync(companyId, roads, "Surfacing", JobCostSource.Subcontractor, 1200m, new DateOnly(2026, 4, 12), "RC-2")).IsSuccess.Should().BeTrue();
        (await servicing.ApportionAsync(companyId, scheme.Id, new DateOnly(2026, 4, 30), "1450", "1460")).Error.Code.Should().Be("Hse.Apportionment");
        (await servicing.SetMeasuresAsync(companyId, small.Id, 15m, null)).IsSuccess.Should().BeTrue();
        (await servicing.SetMeasuresAsync(companyId, large.Id, 20m, null)).IsSuccess.Should().BeTrue();
        IReadOnlyList<ServicingRunLine> april = (await servicing.ApportionAsync(companyId, scheme.Id, new DateOnly(2026, 4, 30), "1450", "1460")).Value;
        april.Select(l => l.Apportioned).Should().Equal(1200m, 3500m);

        // Cost per stand: land plus roads by area (3,000 + 400 / 6,000 + 800) plus water by frontage (1,500 / 2,000).
        IReadOnlyList<StandCostEntry> costs = await servicing.CostPerStandAsync(companyId, scheme.Id);
        costs.Should().Equal(new StandCostEntry("SV-1", 5900m, 4900m), new StandCostEntry("SV-2", 10800m, 8800m), new StandCostEntry("SV-3", 1000m, 0m));
        (await db.Set<ServicingCharge>().Where(c => c.CompanyId == companyId).SumAsync(c => c.Amount)).Should().Be(13700m);
    }

    [SkippableFact]
    public async Task Stands_still_being_serviced_take_their_share_of_the_cost()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer client = new(companyId, "SU-CON", "Council works", Scenario.Usd);
        db.Customers.Add(client);
        LandParcel parcel = new(companyId, $"LP-SU-{companyId}", "Servicing parcel", 3000m, 30000m);
        HousingScheme scheme = new(companyId, $"SCH-SU-{companyId}", "Under-servicing Scheme", SchemeType.CouncilDelivered);
        db.LandParcels.Add(parcel);
        db.HousingSchemes.Add(scheme);
        await db.SaveChangesAsync();
        HousingStand available = new(companyId, parcel.Id, scheme.Id, "SU-1", 300m, 1000m);
        HousingStand underServicing = new(companyId, parcel.Id, scheme.Id, "SU-2", 300m, 1000m, initialStatus: StandStatus.UnderServicing);
        HousingStand sold = new(companyId, parcel.Id, scheme.Id, "SU-3", 300m, 1000m);
        db.HousingStands.AddRange(available, underServicing, sold);
        await db.SaveChangesAsync();
        sold.Sell(client.Id, client.Name, 5000m, new DateOnly(2026, 1, 20));
        await db.SaveChangesAsync();

        IJobCostingService jobs = scope.ServiceProvider.GetRequiredService<IJobCostingService>();
        long roads = (await jobs.CreateContractAsync(companyId, $"SUC-R-{companyId}", "Roads", client.Id, 100_000m, 90_000m, 0m, "4300")).Value;
        (await jobs.CaptureCostAsync(companyId, roads, "Earthworks", JobCostSource.Subcontractor, 900m, new DateOnly(2026, 3, 15), "RU-1")).IsSuccess.Should().BeTrue();
        IServicingCostService servicing = scope.ServiceProvider.GetRequiredService<IServicingCostService>();
        (await servicing.LinkAsync(companyId, scheme.Id, roads, ServicingBasis.Equal)).IsSuccess.Should().BeTrue();
        (await servicing.ApportionAsync(companyId, scheme.Id, new DateOnly(2026, 3, 31), "1450", "1460")).Value.Single().Apportioned.Should().Be(900m);

        // The stand still under servicing shares the cost equally with the available one; the sold stand takes none.
        IReadOnlyList<StandCostEntry> costs = await servicing.CostPerStandAsync(companyId, scheme.Id);
        costs.Should().Equal(new StandCostEntry("SU-1", 1450m, 450m), new StandCostEntry("SU-2", 1450m, 450m), new StandCostEntry("SU-3", 1000m, 0m));
    }
}
