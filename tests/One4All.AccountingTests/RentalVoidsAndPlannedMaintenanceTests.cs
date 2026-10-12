using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers why a void unit is empty and its refurbishment job, and the planned maintenance programme.</summary>
[Collection(AccountingCollection.Name)]
public sealed class RentalVoidsAndPlannedMaintenanceTests
{
    private readonly AccountingFixture _fixture;

    public RentalVoidsAndPlannedMaintenanceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Voids_carry_their_reason_and_planned_work_recurs()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IRentalHousingService rentals = scope.ServiceProvider.GetRequiredService<IRentalHousingService>();
        (await rentals.AddUnitAsync(companyId, "RVP-1", "Flat 1", RentalUnitType.Flat, 2, 300m, "4400", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();

        IRentalMaintenanceService maintenance = scope.ServiceProvider.GetRequiredService<IRentalMaintenanceService>();
        (await maintenance.SetVoidDetailsAsync(companyId, "RVP-1", "Refurbishment", "NO-SUCH-JOB")).Error.Code.Should().Be("Hse.JobNotFound");
        (await maintenance.SetVoidDetailsAsync(companyId, "RVP-1", "Refurbishment", null)).IsSuccess.Should().BeTrue();
        VoidUnit voidUnit = (await rentals.VoidsAsync(companyId, new DateOnly(2026, 1, 31))).Single(v => v.UnitCode == "RVP-1");
        (voidUnit.Reason, voidUnit.DaysVoid).Should().Be(("Refurbishment", 31));

        (await maintenance.PlanAsync(companyId, "RVP-1", "Service geyser", new DateOnly(2026, 2, 1), 6)).IsSuccess.Should().BeTrue();
        long repaint = (await maintenance.PlanAsync(companyId, "RVP-1", "Repaint exterior", new DateOnly(2026, 6, 1), 0)).Value;
        (await maintenance.PlanAsync(companyId, "RVP-1", "Nonsense", new DateOnly(2026, 6, 1), -1)).Error.Code.Should().Be("Hse.Planned");

        IReadOnlyList<PlannedTaskDue> due = (await maintenance.DueAsync(companyId, new DateOnly(2026, 2, 15))).Where(t => t.UnitCode == "RVP-1").ToList();
        due.Should().ContainSingle().Which.Description.Should().Be("Service geyser");
        string repair = (await maintenance.MarkDoneAsync(companyId, due[0].Id, new DateOnly(2026, 2, 10))).Value;
        repair.Should().NotBeNullOrEmpty();
        (await db.Set<PlannedMaintenance>().AsNoTracking().SingleAsync(t => t.Id == due[0].Id)).NextDue.Should().Be(new DateOnly(2026, 8, 1));
        (await maintenance.MarkDoneAsync(companyId, repaint, new DateOnly(2026, 6, 3))).IsSuccess.Should().BeTrue();
        (await maintenance.MarkDoneAsync(companyId, repaint, new DateOnly(2026, 6, 4))).Error.Code.Should().Be("Hse.PlannedNotFound");
        (await maintenance.HistoryAsync(companyId, "RVP-1")).Value.RepairsOpen.Should().Be(2);
    }
}
