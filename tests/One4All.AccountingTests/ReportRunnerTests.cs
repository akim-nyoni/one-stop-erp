using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Receipting;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class ReportRunnerTests
{
    private readonly AccountingFixture _fixture;

    public ReportRunnerTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Runner_renders_gl_ar_and_receipting_reports_against_live_data()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();

            Customer customer = new(companyId, "RPT-C", "Report Customer", Scenario.Usd);
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "RPT-INV", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 1234m, SalesAccountCode = "4000" });

            IAgentService agents = scope.ServiceProvider.GetRequiredService<IAgentService>();
            await agents.RegisterAgentAsync(companyId, "AGENTX", "Report Agent", 5m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();

            // GL — Trial Balance (live period balances).
            string tb = await RenderHtml(runner, companyId, "GL-TB", new() { ["AsAtDate"] = new DateOnly(2026, 3, 31) });
            tb.Should().Contain("Trial Balance");
            tb.Should().Contain("1,234.00"); // the AR control / sales figure appears

            // GL — Income Statement (Sales 1234 in the period).
            string income = await RenderHtml(runner, companyId, "GL-IS", new() { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 3, 31) });
            income.Should().Contain("Income Statement");
            income.Should().Contain("1,234.00");
            income.Should().Contain("Prior year", "the statement shows the same period a year earlier beside it");
            string position = await RenderHtml(runner, companyId, "GL-BS", new() { ["AsAtDate"] = new DateOnly(2026, 3, 31) });
            position.Should().Contain("Prior year").And.Contain("1,234.00");

            // AR — Aged Debtors (the customer with 1234 outstanding).
            string aged = await RenderHtml(runner, companyId, "AR-AGE", new() { ["AsAtDate"] = new DateOnly(2026, 3, 31) });
            aged.Should().Contain("Aged Debtors");
            aged.Should().Contain("Report Customer");
            aged.Should().Contain("1,234.00");

            // Receipting — Agent Collection Reconciliation (RC45), a whole-system RC report.
            string agents = await RenderHtml(runner, companyId, "RC45", new() { ["AsAtDate"] = new DateOnly(2026, 3, 31) });
            agents.Should().Contain("Agent Collection Reconciliation");
            agents.Should().Contain("AGENTX");

            // An unknown code is a clean NotFound.
            var missing = await runner.RenderAsync(companyId, "NOPE-1", new Dictionary<string, object?>(), ReportFormat.Html);
            missing.IsFailure.Should().BeTrue();
            missing.Error.Code.Should().Be("Report.NotFound");

            // CSV export path works too.
            var csv = await runner.RenderAsync(companyId, "AR-AGE", new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 3, 31) }, ReportFormat.Csv);
            csv.IsSuccess.Should().BeTrue(csv.IsFailure ? csv.Error.ToString() : null);
            csv.Value.ContentType.Should().Be("text/csv");
        }
    }

    private static async Task<string> RenderHtml(IReportRunner runner, long companyId, string code, Dictionary<string, object?> parameters)
    {
        var result = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Html);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? $"{code}: {result.Error}" : null);
        result.Value.ContentType.Should().Be("text/html");
        return Encoding.UTF8.GetString(result.Value.Content);
    }
}
