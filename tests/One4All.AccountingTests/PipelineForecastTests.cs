using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Crm;
using One4All.Core.Crm;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers the weighted-pipeline forecast: open opportunities summed by stage, won/lost excluded.</summary>
[Collection(AccountingCollection.Name)]
public sealed class PipelineForecastTests
{
    private readonly AccountingFixture _fixture;

    public PipelineForecastTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Forecast_sums_open_opportunities_by_stage_with_weighting()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            Opportunity lead = new(companyId, "Lead deal", "Alice", 1000m);           // Lead @ 10% → 100
            Opportunity proposal = new(companyId, "Proposal deal", "Bob", 2000m);
            proposal.MoveTo(OpportunityStage.Proposal);                                // @ 50% → 1000
            Opportunity won = new(companyId, "Won deal", "Carol", 5000m);
            won.MoveTo(OpportunityStage.Won);                                          // excluded
            Opportunity lost = new(companyId, "Lost deal", "Dave", 4000m);
            lost.MoveTo(OpportunityStage.Lost);                                        // excluded

            db.AddRange(lead, proposal, won, lost);
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IOpportunityService opportunities = scope.ServiceProvider.GetRequiredService<IOpportunityService>();
            var result = await opportunities.PipelineForecastAsync(companyId);

            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            PipelineForecast forecast = result.Value;

            forecast.Stages.Should().HaveCount(2); // only Lead and Proposal are open
            forecast.Stages.Should().Contain(s => s.Stage == OpportunityStage.Lead && s.Count == 1 && s.EstimatedValue == 1000m && s.WeightedValue == 100m);
            forecast.Stages.Should().Contain(s => s.Stage == OpportunityStage.Proposal && s.Count == 1 && s.EstimatedValue == 2000m && s.WeightedValue == 1000m);

            forecast.TotalEstimated.Should().Be(3000m);
            forecast.TotalWeighted.Should().Be(1100m); // 100 + 1000

            // Conversion: 1 won (5000) and 1 lost → 50% win rate.
            var conversion = await opportunities.ConversionMetricsAsync(companyId);
            conversion.IsSuccess.Should().BeTrue(conversion.IsFailure ? conversion.Error.ToString() : null);
            conversion.Value.Won.Should().Be(1);
            conversion.Value.Lost.Should().Be(1);
            conversion.Value.WonValue.Should().Be(5000m);
            conversion.Value.WinRatePercent.Should().Be(50.0m);
        }
    }
}
