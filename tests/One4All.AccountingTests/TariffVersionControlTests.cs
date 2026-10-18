using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Workflow;
using One4All.Core.Municipal;
using One4All.Core.Security;
using One4All.Core.Workflow;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Fourteenth audit #4: tariff versions keep to unbilled periods, a used version cannot be withdrawn, and a tariff workflow puts a version in force only on approval.</summary>
[Collection(AccountingCollection.Name)]
public sealed class TariffVersionControlTests
{
    private readonly AccountingFixture _fixture;

    public TariffVersionControlTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Versions_respect_billed_periods_and_the_tariff_workflow()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IMunicipalTariffService tariffs = scope.ServiceProvider.GetRequiredService<IMunicipalTariffService>();
        long january = (await tariffs.SaveVersionAsync(companyId, new TariffInput("TVC-REF", new DateOnly(2026, 1, 1), "Refuse", ServiceType.Refuse, TariffType.Flat, "4000", FixedCharge: 10m))).Value;

        // March refuse is billed: a version from 15 March is refused, one from April is fine; the January version cannot be withdrawn.
        BillingRun march = new(companyId, $"TVC-{companyId}", ServiceType.Refuse, new DateOnly(2026, 3, 31), null, "TVC-", 1);
        march.MarkPosted(2);
        db.Add(march);
        await db.SaveChangesAsync();
        (await tariffs.SaveVersionAsync(companyId, new TariffInput("TVC-REF", new DateOnly(2026, 3, 15), FixedCharge: 11m))).Error.Code.Should().Be("Mun.TariffBackdated");
        (await tariffs.WithdrawVersionAsync(companyId, "TVC-REF", new DateOnly(2026, 1, 1))).Error.Code.Should().Be("Mun.TariffVersionUsed");
        (await tariffs.SaveVersionAsync(companyId, new TariffInput("TVC-REF", new DateOnly(2026, 4, 1), FixedCharge: 11m))).IsSuccess.Should().BeTrue();

        // With a tariff workflow, a new version waits out of force; the inbox approval puts it in force.
        string tag = Guid.NewGuid().ToString("N")[..8];
        User engineer = new($"tvc-{tag}", "Town engineer");
        db.Users.Add(engineer);
        await db.SaveChangesAsync();
        WorkflowDefinition definition = new(companyId, WorkflowDocumentTypes.TariffVersion, "Tariff changes");
        definition.AddStage(1, "Council").AddEligibleUser(engineer.Id);
        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();
        long july = (await tariffs.SaveVersionAsync(companyId, new TariffInput("TVC-REF", new DateOnly(2026, 7, 1), FixedCharge: 12m, RequestedByUserId: 5))).Value;
        (await db.Tariffs.AsNoTracking().SingleAsync(t => t.Id == july)).IsActive.Should().BeFalse("it is not in force before approval");
        long instance = await db.WorkflowInstances.AsNoTracking().Where(i => i.CompanyId == companyId && i.DocumentReference == "TV-" + july).Select(i => i.Id).SingleAsync();
        (await scope.ServiceProvider.GetRequiredService<IWorkflowService>().ActAsync(instance, engineer.Id, WorkflowActionType.Approve, null)).Value.Should().Be(WorkflowStatus.Approved);
        (await db.Tariffs.AsNoTracking().SingleAsync(t => t.Id == july)).IsActive.Should().BeTrue();
        (await db.Tariffs.AsNoTracking().SingleAsync(t => t.Id == january)).IsActive.Should().BeTrue();
    }
}
