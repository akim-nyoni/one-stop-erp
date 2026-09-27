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
public sealed class VatReturnByCodeReportTests
{
    private readonly AccountingFixture _fixture;

    public VatReturnByCodeReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Vat_return_breaks_output_and_input_down_by_tax_code()
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
            Customer customer = new(companyId, "VC-C", "Multi-rate Buyer", Scenario.Usd);
            Supplier supplier = new(companyId, "VC-S", "Multi-rate Vendor", Scenario.Usd);
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

            // Standard-rate sale: 1,000 @ 15% output VAT (VAT15).
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "VC-SI1", DocumentDate = new DateOnly(2026, 3, 5),
                Currency = Scenario.Usd, NetAmount = 1_000m, TaxAmount = 150m, SalesAccountCode = "4000", TaxAccountCode = "2200", TaxCode = "VAT15",
            })).IsSuccess.Should().BeTrue();

            // Reduced-rate sale: 500 @ 5% output VAT (VAT5).
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "VC-SI2", DocumentDate = new DateOnly(2026, 3, 6),
                Currency = Scenario.Usd, NetAmount = 500m, TaxAmount = 25m, SalesAccountCode = "4000", TaxAccountCode = "2200", TaxCode = "VAT5",
            })).IsSuccess.Should().BeTrue();

            // Standard-rate purchase: input VAT 60 (VAT15).
            (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
            {
                CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "VC-PI1", DocumentDate = new DateOnly(2026, 3, 7),
                Currency = Scenario.Usd, NetAmount = 400m, TaxAmount = 60m, ExpenseAccountCode = "5000", TaxAccountCode = "2210", TaxCode = "VAT15",
            })).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            VatReturnByCodeReport byCode = await gl.VatReturnByCodeAsync(companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

            VatCodeRow std = byCode.Rows.Single(r => r.TaxCode == "VAT15");
            std.OutputTax.Should().Be(150m);
            std.InputTax.Should().Be(60m);
            std.Net.Should().Be(90m);

            VatCodeRow reduced = byCode.Rows.Single(r => r.TaxCode == "VAT5");
            reduced.OutputTax.Should().Be(25m);
            reduced.InputTax.Should().Be(0m);
            reduced.Net.Should().Be(25m);

            byCode.NetPayable.Should().Be(115m); // 90 + 25

            // The headline VAT return still nets to the same total.
            VatReturnReport headline = await gl.VatReturnAsync(companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
            headline.NetPayable.Should().Be(115m);

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var parameters = new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 3, 1), ["ToDate"] = new DateOnly(2026, 3, 31) };
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "GL-VATC", parameters, ReportFormat.Html)).Value.Content);
            html.Should().Contain("VAT15").And.Contain("VAT5").And.Contain("90.00");
        }
    }
}
