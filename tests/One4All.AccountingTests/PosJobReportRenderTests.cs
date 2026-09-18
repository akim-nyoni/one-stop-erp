using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.JobCosting;
using One4All.Core.Pos;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class PosJobReportRenderTests
{
    private readonly AccountingFixture _fixture;

    public PosJobReportRenderTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Pos_and_job_costing_reports_render_live_figures()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long contractId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();

            // A completed POS sale: 10 @ 5.00 net 50 + tax 7.50 = 57.50.
            Sale sale = new(companyId, terminalId: 1, tillSessionId: 1, cashierUserId: 1, "POS-0001", new DateOnly(2026, 3, 4), Scenario.Usd);
            sale.AddLine(new SaleLine(itemId: 1, "Gadget", warehouseId: 1, quantity: 10m, unitPrice: 5.00m, lineNet: 50m, taxAmount: 7.50m, revenueAccountCode: "4000"));
            db.Set<Sale>().Add(sale);

            // A job-costing client for the certificate.
            Customer client = new(companyId, "JC-CLIENT", "Public Works", Scenario.Usd);
            db.Customers.Add(client);
            await db.SaveChangesAsync();

            IJobCostingService job = scope.ServiceProvider.GetRequiredService<IJobCostingService>();
            contractId = (await job.CreateContractAsync(companyId, "C-500", "Bridge", client.Id, 1_000_000m, 800_000m, 10m, "4300")).Value;
            await job.CaptureCostAsync(companyId, contractId, "Steel", JobCostSource.PurchaseInvoice, 200_000m, new DateOnly(2026, 2, 1), "AP-9");
            await job.CertifyProgressAsync(companyId, contractId, "IPC-9", new DateOnly(2026, 3, 1), 250_000m, 0m, "1250");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var range = new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 3, 31) };

            // POS Sales Summary.
            string pos = await Html(runner, companyId, "POS-SUM", range);
            pos.Should().Contain("POS-0001").And.Contain("57.50");

            // Job Profitability (actual 200k / budget 800k = 25%).
            string profit = await Html(runner, companyId, "JOB-PROF", new());
            profit.Should().Contain("C-500").And.Contain("200,000.00").And.Contain("25.00");

            // Progress Certificates (gross 250k, retention 25k, net 225k).
            string ipc = await Html(runner, companyId, "JOB-IPC", range);
            ipc.Should().Contain("IPC-9").And.Contain("250,000.00").And.Contain("225,000.00");
        }
    }

    private static async Task<string> Html(IReportRunner runner, long companyId, string code, Dictionary<string, object?> parameters)
    {
        var result = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Html);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? $"{code}: {result.Error}" : null);
        return Encoding.UTF8.GetString(result.Value.Content);
    }
}
