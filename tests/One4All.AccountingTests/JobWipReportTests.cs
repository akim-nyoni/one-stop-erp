using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.JobCosting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class JobWipReportTests
{
    private readonly AccountingFixture _fixture;

    public JobWipReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Wip_report_shows_revenue_recognised_versus_certified()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long contractId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer client = new(companyId, "WIP-C", "Developer", Scenario.Usd);
            db.Customers.Add(client);
            await db.SaveChangesAsync();

            IJobCostingService job = scope.ServiceProvider.GetRequiredService<IJobCostingService>();
            // Contract 1,000,000; budget 800,000; cost 200,000 → 25% complete → recognise 250,000.
            contractId = (await job.CreateContractAsync(companyId, "WIP-1", "Office block", client.Id, 1_000_000m, 800_000m, 10m, "4300")).Value;
            await job.CaptureCostAsync(companyId, contractId, "Steel", JobCostSource.PurchaseInvoice, 200_000m, new DateOnly(2026, 2, 1), "AP-W1");
            // Certify only 150,000 gross → under-billed by 100,000.
            (await job.CertifyProgressAsync(companyId, contractId, "IPC-W1", new DateOnly(2026, 3, 1), 100_000m, 50_000m, "1250")).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportDataProvider posJob = scope.ServiceProvider.GetServices<IReportDataProvider>().First(p => p.CanHandle("JobWip"));
            var rows = await posJob.GetRowsAsync(companyId, "JobWip", new Dictionary<string, object?>());
            var row = rows.Single(r => (string)r["ContractNumber"]! == "WIP-1");
            row["PercentComplete"].Should().Be(25m);
            row["RevenueRecognised"].Should().Be(250_000m);
            row["CertifiedToDate"].Should().Be(150_000m);
            row["WipPosition"].Should().Be(100_000m); // under-billed contract asset

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "JOB-WIP", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            html.Should().Contain("WIP-1").And.Contain("250,000.00").And.Contain("100,000.00");
        }
    }
}
