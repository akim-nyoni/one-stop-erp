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
public sealed class VatReturnReportTests
{
    private readonly AccountingFixture _fixture;

    public VatReturnReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Vat_return_nets_output_tax_against_input_tax()
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
            Customer customer = new(companyId, "VAT-C", "Taxed Buyer", Scenario.Usd);
            Supplier supplier = new(companyId, "VAT-S", "Taxed Vendor", Scenario.Usd);
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

            // Sale: net 1,000 + output VAT 150 (Cr 2200).
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "VAT-SI1", DocumentDate = new DateOnly(2026, 3, 10),
                Currency = Scenario.Usd, NetAmount = 1_000m, TaxAmount = 150m, SalesAccountCode = "4000", TaxAccountCode = "2200",
            })).IsSuccess.Should().BeTrue();

            // Purchase: net 400 + input VAT 60 (Dr 2210).
            (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
            {
                CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "VAT-PI1", DocumentDate = new DateOnly(2026, 3, 12),
                Currency = Scenario.Usd, NetAmount = 400m, TaxAmount = 60m, ExpenseAccountCode = "5000", TaxAccountCode = "2210",
            })).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            VatReturnReport vat = await gl.VatReturnAsync(companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
            vat.OutputTax.Should().Be(150m);
            vat.InputTax.Should().Be(60m);
            vat.NetPayable.Should().Be(90m);

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var parameters = new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31) };
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "GL-VAT", parameters, ReportFormat.Html)).Value.Content);
            html.Should().Contain("Net VAT Payable").And.Contain("90.00");
        }
    }
}
