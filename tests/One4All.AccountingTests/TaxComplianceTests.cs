using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers tax compliance: exemption certificates stop tax being charged and are required for exempt invoicing, with expiry warnings; and
/// the VAT return on each basis — invoice date (the ledger), tax point (an invoice delivered last period moved there), and cash accounting
/// (invoice VAT due only as it is paid, part payments releasing their share).
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TaxComplianceTests
{
    private readonly AccountingFixture _fixture;

    public TaxComplianceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Certificates_and_returns_on_each_basis()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer buyer = new(companyId, "TX-BUY", "Ordinary Buyer", Scenario.Usd), embassy = new(companyId, "TX-EMB", "Embassy", Scenario.Usd);
        db.Customers.AddRange(buyer, embassy);
        db.TaxCodes.Add(new TaxCode(companyId, "EXM", "Exempt supply", TaxType.Exempt));
        await db.SaveChangesAsync();

        ITaxComplianceService tax = scope.ServiceProvider.GetRequiredService<ITaxComplianceService>();
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        SalesInvoiceRequest Invoice(long customer, string number, DateOnly date, decimal net, decimal vat, string? taxCode = null, DateOnly? delivered = null) => new()
        {
            CompanyId = companyId, CustomerId = customer, DocumentNumber = number, DocumentDate = date, Currency = Scenario.Usd, NetAmount = net, TaxAmount = vat,
            SalesAccountCode = "4000", TaxAccountCode = vat == 0m ? null : "2200", TaxCode = taxCode, DeliveryDate = delivered,
        };

        // Certificates.
        (await tax.RecordCertificateAsync(companyId, TradingLedger.AccountsReceivable, "TX-EMB", "DIP-77", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), "Ministry")).IsSuccess.Should().BeTrue();
        (await tax.RecordCertificateAsync(companyId, TradingLedger.AccountsReceivable, "NOPE", "X", new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), null)).Error.Code.Should().Be("Tax.TraderNotFound");
        (await ar.PostInvoiceAsync(Invoice(embassy.Id, "TX-E1", new DateOnly(2026, 3, 1), 100m, 15m))).Error.Code.Should().Be("Tax.CustomerExempt");
        (await ar.PostInvoiceAsync(Invoice(embassy.Id, "TX-E2", new DateOnly(2026, 3, 1), 100m, 0m, "EXM"))).IsSuccess.Should().BeTrue();
        (await ar.PostInvoiceAsync(Invoice(embassy.Id, "TX-E3", new DateOnly(2026, 7, 2), 100m, 0m, "EXM"))).Error.Code.Should().Be("Tax.CertificateRequired", "the certificate expired in June");
        (await ar.PostInvoiceAsync(Invoice(buyer.Id, "TX-B0", new DateOnly(2026, 3, 1), 100m, 0m, "EXM"))).Error.Code.Should().Be("Tax.CertificateRequired");
        (await tax.ExpiringAsync(companyId, new DateOnly(2026, 6, 15))).Should().ContainSingle(e => e.TraderCode == "TX-EMB" && e.DaysLeft == 15);
        (await tax.ExpiringAsync(companyId, new DateOnly(2026, 3, 1))).Should().BeEmpty();

        // Invoices: one delivered in February but invoiced in March (tax point February); one invoiced in March, half paid in April.
        (await ar.PostInvoiceAsync(Invoice(buyer.Id, "TX-B1", new DateOnly(2026, 3, 25), 1_000m, 150m, delivered: new DateOnly(2026, 2, 20)))).IsSuccess.Should().BeTrue();
        long b2 = (await ar.PostInvoiceAsync(Invoice(buyer.Id, "TX-B2", new DateOnly(2026, 3, 26), 2_000m, 300m))).Value.OpenItemId;
        (await db.OpenItems.AsNoTracking().SingleAsync(o => o.DocumentNumber == "TX-B1" && o.CompanyId == companyId)).TaxPointDate.Should().Be(new DateOnly(2026, 2, 20));
        long receipt = (await ar.PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = buyer.Id, DocumentNumber = "TX-R1", ReceiptDate = new DateOnly(2026, 4, 10), Currency = Scenario.Usd, Amount = 1_150m, BankAccountCode = "1100",
        })).Value.OpenItemId;
        (await scope.ServiceProvider.GetRequiredService<IAllocationService>().AllocateAsync(companyId, TradingLedger.AccountsReceivable, b2, receipt, 1_150m, new DateOnly(2026, 4, 10))).IsSuccess.Should().BeTrue();

        DateOnly marchStart = new(2026, 3, 1), marchEnd = new(2026, 3, 31), aprilStart = new(2026, 4, 1), aprilEnd = new(2026, 4, 30);
        BasisVatReturn invoiceBasis = await tax.VatReturnAsync(companyId, marchStart, marchEnd, TaxPointBasis.InvoiceDate);
        invoiceBasis.OutputTax.Should().Be(450m);
        invoiceBasis.Adjustments.Should().BeEmpty();

        BasisVatReturn pointMarch = await tax.VatReturnAsync(companyId, marchStart, marchEnd, TaxPointBasis.TaxPoint);
        pointMarch.OutputTax.Should().Be(300m, "TX-B1's tax point is February");
        (await tax.VatReturnAsync(companyId, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), TaxPointBasis.TaxPoint)).OutputTax.Should().Be(150m);

        BasisVatReturn cashMarch = await tax.VatReturnAsync(companyId, marchStart, marchEnd, TaxPointBasis.Cash);
        (cashMarch.LedgerOutput, cashMarch.OutputTax).Should().Be((450m, 0m), "nothing has been paid in March");
        BasisVatReturn cashApril = await tax.VatReturnAsync(companyId, aprilStart, aprilEnd, TaxPointBasis.Cash);
        cashApril.OutputTax.Should().Be(150m, "half of TX-B2 (2 300 gross) was paid, releasing half its 300 VAT");
        cashApril.Adjustments.Should().ContainSingle(a => a.Document == "TX-B2" && a.Output == 150m);

        // The company's own basis is used when none is given.
        (await db.Companies.SingleAsync(c => c.Id == companyId)).TaxPointBasis = TaxPointBasis.Cash;
        await db.SaveChangesAsync();
        (await tax.VatReturnAsync(companyId, aprilStart, aprilEnd)).Basis.Should().Be(TaxPointBasis.Cash);
    }
}
