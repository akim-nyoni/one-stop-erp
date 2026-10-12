using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Crm;
using One4All.Core.Crm;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers moving opportunities through the pipeline and closing them as won or lost.</summary>
[Collection(AccountingCollection.Name)]
public sealed class OpportunityPipelineTests
{
    private readonly AccountingFixture _fixture;

    public OpportunityPipelineTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Opportunities_move_and_close()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        ICrmService crm = scope.ServiceProvider.GetRequiredService<ICrmService>();
        long lead = (await crm.CaptureLeadAsync(new NewLead(companyId, "Tom", "Acme Builders", "tom@acme.example", null, "Web"))).Value;
        (await crm.AdvanceLeadAsync(companyId, lead, Core.Crm.LeadStatus.Qualified)).IsSuccess.Should().BeTrue();
        long won = (await crm.ConvertLeadAsync(companyId, lead, "Acme roofing job", 10_000m, null, null)).Value;
        long lead2 = (await crm.CaptureLeadAsync(new NewLead(companyId, "Sue", "Beta Farms", null, "0771000111", "Referral"))).Value;
        (await crm.AdvanceLeadAsync(companyId, lead2, Core.Crm.LeadStatus.Qualified)).IsSuccess.Should().BeTrue();
        long lost = (await crm.ConvertLeadAsync(companyId, lead2, "Beta irrigation", 4_000m, null, null)).Value;

        IOpportunityService pipeline = scope.ServiceProvider.GetRequiredService<IOpportunityService>();
        DateOnly today = new(2026, 3, 10);
        (await pipeline.MoveAsync(companyId, won, OpportunityStage.Negotiation, null, new DateOnly(2026, 4, 30), null, today)).IsSuccess.Should().BeTrue();
        (await pipeline.MoveAsync(companyId, lost, OpportunityStage.Lost, null, null, " ", today)).Error.Code.Should().Be("Crm.OpportunityMove");
        (await pipeline.MoveAsync(companyId, lost, OpportunityStage.Lost, null, null, "Went with a competitor", today)).IsSuccess.Should().BeTrue();
        (await pipeline.MoveAsync(companyId, lost, OpportunityStage.Proposal, null, null, null, today)).Error.Code.Should().Be("Crm.OpportunityClosed");

        PipelineForecast forecast = (await pipeline.PipelineForecastAsync(companyId)).Value;
        forecast.TotalWeighted.Should().Be(7_500m);
        (await pipeline.MoveAsync(companyId, won, OpportunityStage.Won, null, null, null, new DateOnly(2026, 4, 20))).IsSuccess.Should().BeTrue();
        Opportunity closed = await db.Set<Opportunity>().AsNoTracking().SingleAsync(o => o.Id == won);
        (closed.Stage, closed.ClosedOn, closed.ProbabilityPercent).Should().Be((OpportunityStage.Won, (DateOnly?)new DateOnly(2026, 4, 20), 100m));
        (await db.Set<Opportunity>().AsNoTracking().SingleAsync(o => o.Id == lost)).LostReason.Should().Be("Went with a competitor");
    }
}
