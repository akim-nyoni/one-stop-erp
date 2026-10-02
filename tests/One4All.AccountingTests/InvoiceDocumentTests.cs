using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the customer invoice / credit note document built from the AR open item and its journal: revenue
/// legs become lines, the tax leg becomes VAT, totals and outstanding come from the open item, and
/// non-document items (receipts) are refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class InvoiceDocumentTests
{
    private readonly AccountingFixture _fixture;

    public InvoiceDocumentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Invoice_and_credit_note_render_from_their_open_items()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long invoiceItemId;
        long creditItemId;
        long receiptItemId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "DOC-C", "Document Buyer", Scenario.Usd) { PaymentTermsDays = 30, TaxRegistration = "VAT-C-1" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            var invoice = await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "DOC-INV1", DocumentDate = new DateOnly(2026, 7, 1), Currency = Scenario.Usd,
                NetAmount = 1_000m, TaxAmount = 150m, TaxAccountCode = "2200", TaxCode = "S", SalesAccountCode = "4000", Narrative = "July consultancy",
            });
            invoice.IsSuccess.Should().BeTrue(invoice.IsFailure ? invoice.Error.ToString() : null);
            invoiceItemId = invoice.Value.OpenItemId;

            var credit = await ar.PostCreditNoteAsync(new SalesCreditNoteRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "DOC-CRN1", DocumentDate = new DateOnly(2026, 7, 5), Currency = Scenario.Usd,
                NetAmount = 200m, TaxAmount = 30m, TaxAccountCode = "2200", SalesAccountCode = "4000", Narrative = "Returned goods",
            });
            credit.IsSuccess.Should().BeTrue(credit.IsFailure ? credit.Error.ToString() : null);
            creditItemId = credit.Value.OpenItemId;

            var receipt = await ar.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "DOC-RCT1", ReceiptDate = new DateOnly(2026, 7, 10), Currency = Scenario.Usd,
                Amount = 100m, BankAccountCode = "1100",
            });
            receipt.IsSuccess.Should().BeTrue(receipt.IsFailure ? receipt.Error.ToString() : null);
            receiptItemId = receipt.Value.OpenItemId;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IInvoiceDocumentService documents = scope.ServiceProvider.GetRequiredService<IInvoiceDocumentService>();

            var built = await documents.BuildAsync(companyId, invoiceItemId);
            built.IsSuccess.Should().BeTrue(built.IsFailure ? built.Error.ToString() : null);
            InvoiceDocument doc = built.Value;
            doc.IsCreditNote.Should().BeFalse();
            doc.CustomerCode.Should().Be("DOC-C");
            doc.CustomerTaxNumber.Should().Be("VAT-C-1");
            doc.DocumentNumber.Should().Be("DOC-INV1");
            doc.DueDate.Should().Be(new DateOnly(2026, 7, 31));
            doc.Lines.Should().ContainSingle();
            doc.Lines[0].Amount.Should().Be(1_000m);
            doc.Lines[0].Description.Should().Contain("Sales");
            doc.Tax.Should().Be(150m);
            doc.Gross.Should().Be(1_150m);
            doc.Outstanding.Should().Be(1_150m);
            doc.Narrative.Should().Be("July consultancy");

            string text = (await documents.RenderTextAsync(companyId, invoiceItemId)).Value;
            text.Should().Contain("INVOICE").And.Contain("DOC-INV1").And.Contain("Document Buyer (DOC-C)").And.Contain("1,150.00").And.Contain("Due: 2026-07-31");

            InvoiceDocument crn = (await documents.BuildAsync(companyId, creditItemId)).Value;
            crn.IsCreditNote.Should().BeTrue();
            crn.Net.Should().Be(200m);
            crn.Tax.Should().Be(30m);
            crn.Outstanding.Should().Be(230m);
            (await documents.RenderTextAsync(companyId, creditItemId)).Value.Should().Contain("CREDIT NOTE").And.Contain("Unapplied credit");

            (await documents.BuildAsync(companyId, receiptItemId)).Error.Code.Should().Be("Ar.NotADocument");
            (await documents.BuildAsync(companyId, 999_999)).Error.Code.Should().Be("Ar.ItemNotFound");
        }
    }
}
