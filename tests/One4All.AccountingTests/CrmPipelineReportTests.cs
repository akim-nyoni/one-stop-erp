using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Crm;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class CrmPipelineReportTests
{
    private readonly AccountingFixture _fixture;

    public CrmPipelineReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Pipeline_report_lists_open_opportunities_with_weighted_value()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            Opportunity open = new(companyId, "New HQ fit-out", "Dana Client", 10_000m);
            open.MoveTo(OpportunityStage.Proposal); // 50% default → weighted 5000
            Opportunity won = new(companyId, "Closed deal", "Won Client", 4_000m);
            won.MoveTo(OpportunityStage.Won);
            Opportunity lost = new(companyId, "Lost deal", "Gone Client", 8_000m);
            lost.MoveTo(OpportunityStage.Lost);
            db.Set<Opportunity>().AddRange(open, won, lost);
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string pipe = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "CRM-PIPE", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            pipe.Should().Contain("New HQ fit-out").And.Contain("Proposal");
            pipe.Should().Contain("5,000.00");       // weighted value (50% of 10,000)
            pipe.Should().NotContain("Closed deal");  // Won excluded
            pipe.Should().NotContain("Lost deal");    // Lost excluded
        }
    }
}
