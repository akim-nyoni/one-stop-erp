using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers development covenants: two stands carry two-year covenants from 1 January 2024. One holder submits and gets
/// plans approved (milestones in order; a skipped or back-dated one refused), builds and completes - the covenant is
/// complied with. The other does nothing: the breach run on 2 January 2026 flags it and levies 500 once (a repeat levies
/// nothing); a paid extension of six months (fee 200) reopens it; breached again after the extension, it is levied again
/// and a notice of intention to repossess is issued, after which it cannot be extended. Covenant income is 1,200.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DevelopmentCovenantTests
{
    private readonly AccountingFixture _fixture;

    public DevelopmentCovenantTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Covenants_track_milestones_extensions_and_breaches()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "4978", "Covenant Fees and Levies", AccountType.Income));
        Customer builder = new(companyId, "DV-BLD", "Diligent Builder", Scenario.Usd);
        Customer idle = new(companyId, "DV-IDL", "Idle Holder", Scenario.Usd);
        db.Customers.AddRange(builder, idle);
        LandParcel parcel = new(companyId, $"LP-DV-{companyId}", "Covenant parcel", 5000m, 50000m);
        db.LandParcels.Add(parcel);
        await db.SaveChangesAsync();
        HousingStand built = new(companyId, parcel.Id, null, "DV-1", 300m, 5000m);
        HousingStand empty = new(companyId, parcel.Id, null, "DV-2", 300m, 5000m);
        db.HousingStands.AddRange(built, empty);
        await db.SaveChangesAsync();

        IDevelopmentCovenantService covenants = scope.ServiceProvider.GetRequiredService<IDevelopmentCovenantService>();
        DateOnly start = new(2024, 1, 1);
        string good = (await covenants.CreateAsync(companyId, built.Id, builder.Id, start, 2)).Value;
        string bad = (await covenants.CreateAsync(companyId, empty.Id, idle.Id, start, 2)).Value;
        (await covenants.CreateAsync(companyId, empty.Id, idle.Id, start, 2)).Error.Code.Should().Be("Hse.CovenantOpen");

        (await covenants.RecordMilestoneAsync(companyId, good, CovenantMilestone.PlanApproved, new DateOnly(2024, 3, 1), null)).Error.Code.Should().Be("Hse.MilestoneOrder");
        (await covenants.RecordMilestoneAsync(companyId, good, CovenantMilestone.PlanSubmitted, new DateOnly(2024, 3, 1), "BP-104")).IsSuccess.Should().BeTrue();
        (await covenants.RecordMilestoneAsync(companyId, good, CovenantMilestone.PlanApproved, new DateOnly(2024, 2, 1), null)).Error.Code.Should().Be("Hse.MilestoneDate");
        (await covenants.RecordMilestoneAsync(companyId, good, CovenantMilestone.PlanApproved, new DateOnly(2024, 4, 15), "LIC-BP-9")).IsSuccess.Should().BeTrue();
        (await covenants.RecordMilestoneAsync(companyId, good, CovenantMilestone.ConstructionStarted, new DateOnly(2024, 6, 1), "INSP-1")).IsSuccess.Should().BeTrue();
        (await covenants.RecordMilestoneAsync(companyId, good, CovenantMilestone.Completed, new DateOnly(2025, 9, 30), "INSP-2")).IsSuccess.Should().BeTrue();

        DateOnly firstRun = new(2026, 1, 2);
        (await covenants.RunBreachesAsync(companyId, firstRun, 500m, "4978")).Value.Should().Equal(bad);
        (await covenants.RunBreachesAsync(companyId, firstRun, 500m, "4978")).Value.Should().BeEmpty();
        (await covenants.GrantExtensionAsync(companyId, bad, 6, new DateOnly(2026, 1, 10), 200m, "4978")).Value.Should().Be(new DateOnly(2026, 6, 30));
        (await covenants.RunBreachesAsync(companyId, new DateOnly(2026, 7, 1), 500m, "4978")).Value.Should().Equal(bad);
        (await covenants.IssueNoticeAsync(companyId, good, new DateOnly(2026, 7, 2))).Error.Code.Should().Be("Hse.NotBreached");
        (await covenants.IssueNoticeAsync(companyId, bad, new DateOnly(2026, 7, 2))).IsSuccess.Should().BeTrue();
        (await covenants.GrantExtensionAsync(companyId, bad, 6, new DateOnly(2026, 7, 3), 200m, "4978")).Error.Code.Should().Be("Hse.CovenantClosed");

        (await covenants.RegisterAsync(companyId)).Should().Equal(
            new CovenantEntry(good, "DV-1", new DateOnly(2025, 12, 31), CovenantMilestone.Completed, 0, CovenantStatus.Complied),
            new CovenantEntry(bad, "DV-2", new DateOnly(2026, 6, 30), null, 1, CovenantStatus.NoticeIssued));

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 7, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "4978").Credit.Should().Be(1200m);
    }
}
