using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class ControlReconciliationReportTests
{
    private readonly AccountingFixture _fixture;

    public ControlReconciliationReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Reconciliation_shows_gl_control_agreeing_with_sub_ledger()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "CTRL-C", "Recon Buyer", Scenario.Usd);
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "CTRL-SI1", DocumentDate = new DateOnly(2026, 3, 10),
                Currency = Scenario.Usd, NetAmount = 1_000m, TaxAmount = 0m, SalesAccountCode = "4000",
            })).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            ControlReconciliationReport recon = await gl.ControlReconciliationAsync(companyId, new DateOnly(2026, 3, 31));

            ControlReconciliationRow arRow = recon.Rows.Single(r => r.Control == "Accounts Receivable");
            arRow.GlBalance.Should().Be(1_000m);
            arRow.SubLedgerBalance.Should().Be(1_000m);
            arRow.Difference.Should().Be(0m);

            // Every control account reconciles (difference zero) after clean posting.
            recon.Rows.Should().OnlyContain(r => r.Difference == 0m);

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var parameters = new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 3, 31) };
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "GL-CTRL", parameters, ReportFormat.Html)).Value.Content);
            html.Should().Contain("Accounts Receivable").And.Contain("1,000.00");
        }
    }
}
