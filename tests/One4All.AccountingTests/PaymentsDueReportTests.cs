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
public sealed class PaymentsDueReportTests
{
    private readonly AccountingFixture _fixture;

    public PaymentsDueReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Payments_due_lists_outstanding_invoices_by_due_date()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier supplier = new(companyId, "PD-SUP", "Due Supplier", Scenario.Usd) { PaymentTermsDays = 30 };
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            supplierId = supplier.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            // Overdue: invoiced 2026-02-01, due 2026-03-03.
            await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "PD-1", DocumentDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, NetAmount = 400m, ExpenseAccountCode = "5000" });
            // Due later: invoiced 2026-04-20, due 2026-05-20.
            await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "PD-2", DocumentDate = new DateOnly(2026, 4, 20), Currency = Scenario.Usd, NetAmount = 250m, ExpenseAccountCode = "5000" });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            PaymentsDueReport report = await reports.PaymentsDueAsync(companyId, new DateOnly(2026, 4, 30));

            report.Rows.Should().HaveCount(2);
            report.Total.Should().Be(650m);
            // Earliest due date first (the overdue PD-1), with negative days until due.
            report.Rows[0].DocumentNumber.Should().Be("PD-1");
            report.Rows[0].Outstanding.Should().Be(400m);
            report.Rows[0].DaysUntilDue.Should().BeLessThan(0);
            report.Rows[1].DocumentNumber.Should().Be("PD-2");
            report.Rows[1].DaysUntilDue.Should().BeGreaterThan(0);

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var parameters = new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 4, 30) };
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "AP-DUE", parameters, ReportFormat.Html)).Value.Content);
            html.Should().Contain("PD-1").And.Contain("Due Supplier").And.Contain("400.00");
        }
    }
}
