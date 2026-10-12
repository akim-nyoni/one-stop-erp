using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Restaurant;
using One4All.Core.Restaurant;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers placing tables on the floor plan and flagging tabs open too long.</summary>
[Collection(AccountingCollection.Name)]
public sealed class FloorPlanTests
{
    private readonly AccountingFixture _fixture;

    public FloorPlanTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Tables_are_placed_and_long_tabs_flagged()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        RestaurantTable t1 = new(companyId, "FP-1", 4), t2 = new(companyId, "FP-2", 2);
        db.AddRange(t1, t2);
        await db.SaveChangesAsync();
        TableOrder tab = new(companyId, t1.Id, "FP-ORD-1", 1, new DateOnly(2026, 3, 1), 3);
        db.Add(tab);
        await db.SaveChangesAsync();

        IFloorPlanService floor = scope.ServiceProvider.GetRequiredService<IFloorPlanService>();
        (await floor.PlaceTableAsync(companyId, "fp-1", 2, 3, TableShape.Round)).IsSuccess.Should().BeTrue();
        (await floor.PlaceTableAsync(companyId, "FP-2", 2, 3, TableShape.Square)).Error.Code.Should().Be("Res.PlanTaken");
        (await floor.PlaceTableAsync(companyId, "FP-2", 100, 3, TableShape.Square)).Error.Code.Should().Be("Res.PlanPlace");
        (await floor.SetTabWarningsAsync(companyId, 60, 0m)).IsSuccess.Should().BeTrue();

        DateTimeOffset soon = tab.CreatedOnUtc.AddMinutes(30), late = tab.CreatedOnUtc.AddMinutes(95);
        (await floor.WarningsAsync(companyId, soon)).Should().BeEmpty();
        FloorTable flagged = (await floor.WarningsAsync(companyId, late)).Should().ContainSingle().Subject;
        (flagged.TableNumber, flagged.OrderNumber, flagged.X, flagged.Shape).Should().Be(("FP-1", "FP-ORD-1", (int?)2, TableShape.Round));
        flagged.Warning.Should().Be("open 95 min");
        (await floor.PlanAsync(companyId, late)).Single(t => t.TableNumber == "FP-2").OrderNumber.Should().BeNull();
    }
}
