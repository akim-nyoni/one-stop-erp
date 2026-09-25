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
public sealed class CreditExposureReportTests
{
    private readonly AccountingFixture _fixture;

    public CreditExposureReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Credit_exposure_shows_limit_balance_available_and_flags()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long withinId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer within = new(companyId, "CE-OK", "Within Limit", Scenario.Usd) { CreditLimit = 1000m };
            Customer held = new(companyId, "CE-HOLD", "Held Buyer", Scenario.Usd) { CreditLimit = 500m, OnHold = true };
            db.Customers.AddRange(within, held);
            await db.SaveChangesAsync();
            withinId = within.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = withinId, DocumentNumber = "CE-1", DocumentDate = new DateOnly(2026, 4, 30),
                Currency = Scenario.Usd, NetAmount = 600m, TaxAmount = 0m, SalesAccountCode = "4000",
            })).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            CreditExposureReport report = await reports.CreditExposureAsync(companyId, new DateOnly(2026, 4, 30));

            CreditExposureRow ok = report.Rows.Single(r => r.CustomerCode == "CE-OK");
            ok.CreditLimit.Should().Be(1000m);
            ok.Balance.Should().Be(600m);
            ok.Available.Should().Be(400m);
            ok.OverLimit.Should().BeFalse();
            ok.OnHold.Should().BeFalse();

            // The held customer has a finite limit and no balance, but still appears (on hold).
            CreditExposureRow held = report.Rows.Single(r => r.CustomerCode == "CE-HOLD");
            held.OnHold.Should().BeTrue();
            held.Balance.Should().Be(0m);

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var parameters = new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 4, 30) };
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "AR-CREDIT", parameters, ReportFormat.Html)).Value.Content);
            html.Should().Contain("Within Limit").And.Contain("600.00").And.Contain("400.00");
            html.Should().Contain("Held Buyer");
        }
    }
}
