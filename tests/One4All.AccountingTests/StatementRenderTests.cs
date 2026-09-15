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
public sealed class StatementRenderTests
{
    private readonly AccountingFixture _fixture;

    public StatementRenderTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Customer_statement_renders_open_items_and_balance()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "STMT-C", "Statement Buyer", Scenario.Usd);
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "SI-1", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 100m, TaxAmount = 15m, SalesAccountCode = "4000", TaxAccountCode = "2200" });
            await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "PR-1", ReceiptDate = new DateOnly(2026, 3, 10), Currency = Scenario.Usd, Amount = 50m, BankAccountCode = "1100" });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IFinancialReportRenderer renderer = scope.ServiceProvider.GetRequiredService<IFinancialReportRenderer>();
            var html = await renderer.RenderCustomerStatementAsync(companyId, customerId, ReportFormat.Html);
            html.IsSuccess.Should().BeTrue(html.IsFailure ? html.Error.ToString() : null);
            string text = Encoding.UTF8.GetString(html.Value.Content);
            text.Should().Contain("Statement of Account");
            text.Should().Contain("STMT-C");
            text.Should().Contain("SI-1");
            text.Should().Contain("65.00"); // 115 invoice − 50 receipt = balance due

            var pdf = await renderer.RenderCustomerStatementAsync(companyId, customerId, ReportFormat.Pdf);
            Encoding.ASCII.GetString(pdf.Value.Content, 0, 5).Should().Be("%PDF-");
        }
    }
}
