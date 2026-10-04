using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Housing;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the layout plan register: a parcel's layout is submitted once (amendments are versions); it is approved by the
/// planning authority; it cannot be amended while a version awaits approval; an amendment keeps the approved version in
/// force until it is itself approved; the history shows what each version changed; and the plan in force is reconciled
/// against the stands on the register.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class LayoutPlanTests
{
    private readonly AccountingFixture _fixture;

    public LayoutPlanTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Layouts_are_versioned_approved_and_reconciled_to_the_stand_register()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        LandParcel parcel = new(companyId, $"LP-LAY-{companyId}", "Layout parcel", 50000m, 100000m);
        db.LandParcels.Add(parcel);
        await db.SaveChangesAsync();
        db.HousingStands.AddRange(Enumerable.Range(1, 11).Select(i => new HousingStand(companyId, parcel.Id, null, $"LAY-{i}", 300m, 1000m)));
        await db.SaveChangesAsync();

        ILayoutPlanService layouts = scope.ServiceProvider.GetRequiredService<ILayoutPlanService>();
        LayoutSubmission Submission(Dictionary<StandUse, int> uses, string? reason = null, int day = 1) => new()
        {
            CompanyId = companyId, PlanNumber = "tpl-77", LandParcelId = parcel.Id, Planner = "T. Planner", Density = PlanDensity.High, Uses = uses,
            Date = new DateOnly(2026, 2, day), AmendmentReason = reason,
        };
        Dictionary<StandUse, int> original = new() { [StandUse.Residential] = 8, [StandUse.Commercial] = 1, [StandUse.OpenSpace] = 1, [StandUse.Roads] = 3 };
        (await layouts.SubmitAsync(Submission(new() { [StandUse.Roads] = 3 }))).Error.Code.Should().Be("Hse.Layout");
        (await layouts.SubmitAsync(Submission(original))).IsSuccess.Should().BeTrue();
        (await layouts.SubmitAsync(Submission(original))).Error.Code.Should().Be("Hse.LayoutExists");
        (await layouts.ReconcileAsync(companyId, "TPL-77")).Error.Code.Should().Be("Hse.LayoutNotApproved");
        (await layouts.AmendAsync(Submission(original, "Too early"))).Error.Code.Should().Be("Hse.LayoutPending");

        (await layouts.ApproveAsync(companyId, "TPL-77", "Town Planning Committee", new DateOnly(2026, 3, 1))).IsSuccess.Should().BeTrue();
        (await layouts.ApproveAsync(companyId, "TPL-77", "Town Planning Committee", new DateOnly(2026, 3, 2))).Error.Code.Should().Be("Hse.LayoutApproved");

        // Ten stands planned, eleven on the register: one too many.
        LayoutReconciliation first = (await layouts.ReconcileAsync(companyId, "TPL-77")).Value;
        first.Should().Be(new LayoutReconciliation("TPL-77", 1, 10, 11, 1));

        // The amendment adds a residential stand and a clinic site and drops the open space; version 1 stays in force until approval.
        Dictionary<StandUse, int> amended = new() { [StandUse.Residential] = 9, [StandUse.Commercial] = 1, [StandUse.Clinics] = 1, [StandUse.Roads] = 3 };
        (await layouts.AmendAsync(Submission(amended))).Error.Code.Should().Be("Hse.Layout");
        (await layouts.AmendAsync(Submission(amended, "Clinic site required by the health department", 20))).Value.Should().Be(2);
        (await layouts.ReconcileAsync(companyId, "TPL-77")).Value.VersionInForce.Should().Be(1);
        (await layouts.ApproveAsync(companyId, "TPL-77", "Town Planning Committee", new DateOnly(2026, 4, 1))).IsSuccess.Should().BeTrue();
        (await layouts.ReconcileAsync(companyId, "TPL-77")).Value.Should().Be(new LayoutReconciliation("TPL-77", 2, 11, 11, 0));

        IReadOnlyList<LayoutVersionEntry> history = (await layouts.HistoryAsync(companyId, "tpl-77")).Value;
        history.Select(h => (h.Version, h.Stands, h.ApprovedOn)).Should().Equal((1, 10, (DateOnly?)new DateOnly(2026, 3, 1)), (2, 11, (DateOnly?)new DateOnly(2026, 4, 1)));
        history[0].Changes.Should().BeEmpty();
        history[1].Changes.Should().Equal(new LayoutChange(StandUse.Residential, 8, 9), new LayoutChange(StandUse.OpenSpace, 1, 0), new LayoutChange(StandUse.Clinics, 0, 1));
        history[1].AmendmentReason.Should().Be("Clinic site required by the health department");
    }
}
