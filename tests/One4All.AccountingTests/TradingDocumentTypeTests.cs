using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the document type recorded on trading journals: each kind of sales and purchase document now names itself
/// (it used to record an account code such as "4000" or "1100"), which is what the account enquiry and audit trail show.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TradingDocumentTypeTests
{
    private readonly AccountingFixture _fixture;

    public TradingDocumentTypeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Trading_journals_carry_their_document_type()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "TDT-C", "Customer", Scenario.Usd);
        Supplier supplier = new(companyId, "TDT-S", "Supplier", Scenario.Usd);
        db.AddRange(customer, supplier);
        await db.SaveChangesAsync();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        DateOnly date = new(2026, 3, 10);

        Dictionary<string, string> journals = new()
        {
            ["SalesInvoice"] = (await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "TDT-I", DocumentDate = date, Currency = Scenario.Usd, NetAmount = 100m, SalesAccountCode = "4000" })).Value.JournalNumber,
            ["SalesCreditNote"] = (await ar.PostCreditNoteAsync(new SalesCreditNoteRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "TDT-CN", DocumentDate = date, Currency = Scenario.Usd, NetAmount = 10m, SalesAccountCode = "4000" })).Value.JournalNumber,
            ["CustomerReceipt"] = (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "TDT-R", ReceiptDate = date, Currency = Scenario.Usd, Amount = 50m, BankAccountCode = "1100" })).Value.JournalNumber,
            ["PurchaseInvoice"] = (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "TDT-PI", DocumentDate = date, Currency = Scenario.Usd, NetAmount = 80m, ExpenseAccountCode = "6800" })).Value.JournalNumber,
            ["PurchaseCreditNote"] = (await ap.PostCreditNoteAsync(new PurchaseCreditNoteRequest { CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "TDT-PCN", DocumentDate = date, Currency = Scenario.Usd, NetAmount = 8m, ExpenseAccountCode = "6800" })).Value.JournalNumber,
            ["SupplierPayment"] = (await ap.PostPaymentAsync(new PaymentRequest { CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "TDT-P", PaymentDate = date, Currency = Scenario.Usd, Amount = 40m, BankAccountCode = "1100" })).Value.JournalNumber,
        };

        Dictionary<string, string> recorded = await db.Journals.AsNoTracking()
            .Where(j => j.CompanyId == companyId && journals.Values.Contains(j.JournalNumber))
            .ToDictionaryAsync(j => j.JournalNumber, j => j.DocumentType ?? string.Empty);

        foreach ((string documentType, string journalNumber) in journals)
        {
            recorded[journalNumber].Should().Be(documentType);
        }
    }
}
