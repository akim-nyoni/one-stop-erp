using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Sales invoices and credit notes carry unique serial numbers (a blank number takes the next from the "SINV" / "SCN" sequence; a number used twice is
/// refused), and a credit note carries its tax code and the invoice it reverses: its VAT is worked out from the code and tagged with it, it cannot credit
/// more than the invoice less earlier credits, it settles what is unpaid on the invoice, and the fiscal device gets the invoice as its reference.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SalesDocumentNumberingTests
{
    private readonly AccountingFixture _fixture;

    public SalesDocumentNumberingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Blank_numbers_come_from_the_sequence_and_duplicates_are_refused()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer first = new(companyId, "SN-C1", "First", Scenario.Usd);
        Customer second = new(companyId, "SN-C2", "Second", Scenario.Usd);
        db.AddRange(first, second);
        await db.SaveChangesAsync();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        DateOnly day = new(2026, 4, 1);
        SalesInvoiceRequest Invoice(long customer, string number) => new()
        {
            CompanyId = companyId, CustomerId = customer, DocumentNumber = number, DocumentDate = day, Currency = Scenario.Usd, NetAmount = 100m, SalesAccountCode = "4000",
        };

        // Blank numbers are taken from the SINV and SCN sequences, one after the other.
        TradingPostingResult a = (await ar.PostInvoiceAsync(Invoice(first.Id, string.Empty))).Value;
        TradingPostingResult b = (await ar.PostInvoiceAsync(Invoice(second.Id, "  "))).Value;
        TradingPostingResult credit = (await ar.PostCreditNoteAsync(new SalesCreditNoteRequest
        {
            CompanyId = companyId, CustomerId = first.Id, DocumentDate = day, Currency = Scenario.Usd, NetAmount = 10m, SalesAccountCode = "4000",
        })).Value;
        (await NumberOf(db, a.OpenItemId), await NumberOf(db, b.OpenItemId), await NumberOf(db, credit.OpenItemId)).Should().Be(("SINV-00001", "SINV-00002", "SCN-00001"));

        // A typed number is kept; the same invoice number again is refused, on any customer of the company; a credit note may share an invoice's number.
        (await ar.PostInvoiceAsync(Invoice(first.Id, " SN-100 "))).IsSuccess.Should().BeTrue();
        Result<TradingPostingResult> again = await ar.PostInvoiceAsync(Invoice(second.Id, "SN-100"));
        again.Error.Code.Should().Be("Ar.DuplicateDocument");
        (await ar.PostInvoiceAsync(Invoice(second.Id, "SINV-00001"))).Error.Code.Should().Be("Ar.DuplicateDocument");
        (await ar.PostCreditNoteAsync(new SalesCreditNoteRequest
        {
            CompanyId = companyId, CustomerId = second.Id, DocumentNumber = "SCN-00001", DocumentDate = day, Currency = Scenario.Usd, NetAmount = 5m, SalesAccountCode = "4000",
        })).Error.Code.Should().Be("Ar.DuplicateDocument");
        (await ar.PostCreditNoteAsync(new SalesCreditNoteRequest
        {
            CompanyId = companyId, CustomerId = first.Id, DocumentNumber = "SN-100", DocumentDate = day, Currency = Scenario.Usd, NetAmount = 5m, SalesAccountCode = "4000",
        })).IsSuccess.Should().BeTrue();
        (await db.OpenItems.CountAsync(o => o.CompanyId == companyId && o.ItemType == OpenItemType.Invoice)).Should().Be(3, "the refused invoices left nothing behind");
    }

    [SkippableFact]
    public async Task A_credit_note_takes_its_tax_code_and_original_invoice()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        SimulatedFiscalDeviceProvider device = new();
        await using ServiceProvider provider = _fixture.BuildServiceProvider(s => s.AddSingleton<IFiscalDeviceProvider>(device));
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        (await scope.ServiceProvider.GetRequiredService<IMasterDataService>().CreateTaxCodeAsync(new NewTaxCode(companyId, "CN-STD", "Standard", TaxType.Standard, 15m,
            new DateOnly(2026, 1, 1), "2200", "2210"))).IsSuccess.Should().BeTrue();
        Customer buyer = new(companyId, "CN-C", "Buyer", Scenario.Usd) { TaxRegistration = "VAT-12" };
        Customer other = new(companyId, "CN-O", "Other", Scenario.Usd);
        db.AddRange(buyer, other);
        await db.SaveChangesAsync();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        DateOnly day = new(2026, 5, 4);
        TradingPostingResult invoice = (await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = buyer.Id, DocumentNumber = "CN-INV1", DocumentDate = day, Currency = Scenario.Usd, NetAmount = 200m, TaxCode = "CN-STD", SalesAccountCode = "4000",
        })).Value;
        (await db.OpenItems.AsNoTracking().SingleAsync(o => o.Id == invoice.OpenItemId)).OriginalTransaction.Should().Be(230m);
        SalesCreditNoteRequest Credit(string number, decimal net, string? original = "CN-INV1", long? customer = null) => new()
        {
            CompanyId = companyId, CustomerId = customer ?? buyer.Id, DocumentNumber = number, DocumentDate = day, Currency = Scenario.Usd, NetAmount = net, TaxCode = "cn-std",
            SalesAccountCode = "4000", OriginalInvoiceNumber = original,
        };

        // 80 net under CN-STD: 12 VAT worked out, on a tax line tagged CN-STD; the 92 settles that much of the invoice; the device gets the invoice as the reference.
        TradingPostingResult credit = (await ar.PostCreditNoteAsync(Credit("CN-CR1", 80m))).Value;
        List<JournalLine> lines = await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.JournalNumber == credit.JournalNumber).SelectMany(j => j.Lines).ToListAsync();
        JournalLine vat = lines.Single(l => l.AccountCode == "2200");
        (vat.BaseDebit, vat.TaxCode).Should().Be((12m, "CN-STD"));
        OpenItem note = await db.OpenItems.AsNoTracking().SingleAsync(o => o.Id == credit.OpenItemId);
        (note.OriginalTransaction, note.OutstandingTransaction, note.ReferenceDocumentNumber).Should().Be((92m, 0m, "CN-INV1"));
        (await db.OpenItems.AsNoTracking().SingleAsync(o => o.Id == invoice.OpenItemId)).OutstandingTransaction.Should().Be(138m);
        device.Signed.Should().Contain(r => r.Kind == FiscalDocumentKind.CreditNote && r.DocumentNumber == "CN-CR1" && r.ReferenceDocumentNumber == "CN-INV1" && r.TotalGross == 92m
            && r.TotalTax == 12m && r.CustomerTaxNumber == "VAT-12");

        // 138 is left to credit: 130 net (149.50 gross) is too much, as is another customer's or an unknown invoice; 120 net (138) is exactly what is left.
        (await ar.PostCreditNoteAsync(Credit("CN-CR2", 130m))).Error.Code.Should().Be("Ar.CreditExceedsInvoice");
        (await ar.PostCreditNoteAsync(Credit("CN-CR2", 10m, customer: other.Id))).Error.Code.Should().Be("Ar.OriginalInvoice");
        (await ar.PostCreditNoteAsync(Credit("CN-CR2", 10m, "CN-NONE"))).Error.Code.Should().Be("Ar.OriginalInvoice");
        (await ar.PostCreditNoteAsync(Credit("CN-CR2", 120m))).IsSuccess.Should().BeTrue();
        (await ar.PostCreditNoteAsync(Credit("CN-CR3", 1m))).Error.Code.Should().Be("Ar.CreditExceedsInvoice", "the invoice is fully credited");
        (await db.OpenItems.AsNoTracking().SingleAsync(o => o.Id == invoice.OpenItemId)).OutstandingTransaction.Should().Be(0m);
    }

    private static Task<string> NumberOf(One4AllDbContext db, long openItemId)
        => db.OpenItems.AsNoTracking().Where(o => o.Id == openItemId).Select(o => o.DocumentNumber).SingleAsync();
}
