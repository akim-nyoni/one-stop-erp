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
public sealed class CashFlowForecastTests
{
    private readonly AccountingFixture _fixture;

    public CashFlowForecastTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Forecast_buckets_receivables_and_payables_by_due_date()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long supplierId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "CF-CUST", "Forecast Buyer", Scenario.Usd) { PaymentTermsDays = 30 };
            Supplier supplier = new(companyId, "CF-SUP", "Forecast Supplier", Scenario.Usd) { PaymentTermsDays = 60 };
            db.Customers.Add(customer);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            supplierId = supplier.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();

            // Overdue receivable: invoiced 2026-02-01, 30-day terms → due 2026-03-03 (before as-at).
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "CF-AR0", DocumentDate = new DateOnly(2026, 2, 1),
                Currency = Scenario.Usd, NetAmount = 500m, TaxAmount = 0m, SalesAccountCode = "4000",
            })).IsSuccess.Should().BeTrue();

            // Receivable due within 30 days: invoiced 2026-04-30 → due 2026-05-30.
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "CF-AR1", DocumentDate = new DateOnly(2026, 4, 30),
                Currency = Scenario.Usd, NetAmount = 1_000m, TaxAmount = 0m, SalesAccountCode = "4000",
            })).IsSuccess.Should().BeTrue();

            // Payable due in 31-60 days: invoiced 2026-04-30, 60-day terms → due 2026-06-29.
            (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
            {
                CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "CF-AP1", DocumentDate = new DateOnly(2026, 4, 30),
                Currency = Scenario.Usd, NetAmount = 800m, ExpenseAccountCode = "5000",
            })).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            CashFlowForecastReport forecast = await reports.CashFlowForecastAsync(companyId, new DateOnly(2026, 4, 30));

            CashFlowForecastRow overdue = forecast.Rows.Single(r => r.Bucket == "Overdue");
            overdue.Inflow.Should().Be(500m);
            overdue.Net.Should().Be(500m);
            overdue.RunningNet.Should().Be(500m);

            CashFlowForecastRow due30 = forecast.Rows.Single(r => r.Bucket == "Due 0-30");
            due30.Inflow.Should().Be(1_000m);
            due30.RunningNet.Should().Be(1_500m);

            CashFlowForecastRow due60 = forecast.Rows.Single(r => r.Bucket == "Due 31-60");
            due60.Outflow.Should().Be(800m);
            due60.Net.Should().Be(-800m);
            due60.RunningNet.Should().Be(700m); // 500 + 1000 - 800

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var parameters = new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 4, 30) };
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "CB-FLOW", parameters, ReportFormat.Html)).Value.Content);
            html.Should().Contain("Overdue").And.Contain("Due 31-60").And.Contain("800.00");
        }
    }
}
