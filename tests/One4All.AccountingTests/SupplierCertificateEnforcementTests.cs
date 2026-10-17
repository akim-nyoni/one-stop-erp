using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers certificates on the supplier side: an exempt purchase needs the supplier's exemption certificate and a certified supply carries
/// no tax; a supplier's tax clearance certificate (not only its clearance date) frees its payments from withholding, until revoked; and
/// the company's default withholding code is deducted from payments to uncleared suppliers that name none - single payments and payment
/// runs alike.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SupplierCertificateEnforcementTests
{
    private readonly AccountingFixture _fixture;

    public SupplierCertificateEnforcementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Supplier_certificates_are_enforced()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "2496", "Withholding Tax Payable", AccountType.Liability));
        TaxCode wht = new(companyId, "WHT10", "Withholding 10%", TaxType.Withholding) { OutputAccountCode = "2496", IsActive = true };
        wht.AddRate(new DateOnly(2026, 1, 1), 10m);
        db.TaxCodes.AddRange(wht, new TaxCode(companyId, "PEX", "Exempt purchase", TaxType.Exempt));
        Supplier ngoSupplier = new(companyId, "SC-NGO", "Relief Supplies", Scenario.Usd) { BankAccountNumber = "100" };
        Supplier builder = new(companyId, "SC-BLD", "Builder", Scenario.Usd) { BankAccountNumber = "200" };
        Supplier certified = new(companyId, "SC-CRT", "Certified Contractor", Scenario.Usd) { BankAccountNumber = "300" };
        db.Suppliers.AddRange(ngoSupplier, builder, certified);
        await db.SaveChangesAsync();

        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        ITaxComplianceService tax = scope.ServiceProvider.GetRequiredService<ITaxComplianceService>();
        PurchaseInvoiceRequest Invoice(Supplier supplier, string number, decimal net, decimal taxAmount = 0m, string? code = null) => new()
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd, NetAmount = net, ExpenseAccountCode = "6800",
            TaxAmount = taxAmount, TaxAccountCode = taxAmount == 0m ? null : "2200", TaxCode = code,
        };

        // Exempt purchases need the supplier's certificate; under one, no tax.
        (await ap.PostInvoiceAsync(Invoice(ngoSupplier, "SC-1", 500m, code: "PEX"))).Error.Code.Should().Be("Tax.CertificateRequired");
        (await tax.RecordCertificateAsync(companyId, TradingLedger.AccountsPayable, "sc-ngo", "EXM-2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "Revenue authority")).IsSuccess.Should().BeTrue();
        (await ap.PostInvoiceAsync(Invoice(ngoSupplier, "SC-1", 500m, code: "PEX"))).IsSuccess.Should().BeTrue();
        (await ap.PostInvoiceAsync(Invoice(ngoSupplier, "SC-2", 500m, 75m))).Error.Code.Should().Be("Tax.SupplierExempt");
        (await ap.PostInvoiceAsync(Invoice(builder, "SC-3", 1_000m, 150m))).IsSuccess.Should().BeTrue();

        // Clearance certificates are for suppliers only.
        (await tax.RecordCertificateAsync(companyId, TradingLedger.AccountsReceivable, "X", "TC-1", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null, CertificatePurpose.TaxClearance))
            .Error.Code.Should().Be("Tax.ClearanceForSuppliers");
        long clearance = (await tax.RecordCertificateAsync(companyId, TradingLedger.AccountsPayable, "SC-CRT", "TC-77", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), "Revenue authority",
            CertificatePurpose.TaxClearance)).Value;
        (await ap.PostInvoiceAsync(Invoice(certified, "SC-4", 800m))).IsSuccess.Should().BeTrue();
        (await ap.PostInvoiceAsync(Invoice(certified, "SC-5", 600m))).IsSuccess.Should().BeTrue();

        // No company default yet: a payment that names no code withholds nothing.
        PaymentRequest Pay(Supplier supplier, string number, decimal amount, DateOnly date, string? code = null) => new()
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = number, PaymentDate = date, Currency = Scenario.Usd, Amount = amount, BankAccountCode = "1100", WithholdingTaxCode = code,
        };
        (await ap.PostPaymentAsync(Pay(builder, "SC-P1", 100m, new DateOnly(2026, 4, 1)))).Value.WithheldBase.Should().Be(0m);

        // With the default set, the uncleared builder has 10% withheld; the certified contractor has none.
        (await db.Companies.FindAsync(companyId))!.WithholdingWithoutClearanceTaxCode = "WHT10";
        await db.SaveChangesAsync();
        (await ap.PostPaymentAsync(Pay(builder, "SC-P2", 200m, new DateOnly(2026, 4, 2)))).Value.WithheldBase.Should().Be(20m);
        (await ap.PostPaymentAsync(Pay(certified, "SC-P3", 300m, new DateOnly(2026, 4, 2)))).Value.WithheldBase.Should().Be(0m);

        // Revoked, the clearance no longer protects later payments.
        (await tax.RevokeCertificateAsync(companyId, clearance, new DateOnly(2026, 5, 1))).IsSuccess.Should().BeTrue();
        (await ap.PostPaymentAsync(Pay(certified, "SC-P4", 100m, new DateOnly(2026, 5, 2)))).Value.WithheldBase.Should().Be(10m);

        // The payment run applies the same rule and the expiry list shows what each certificate is for.
        IPaymentRunService run = scope.ServiceProvider.GetRequiredService<IPaymentRunService>();
        PaymentRunRequest request = new() { CompanyId = companyId, PaymentDate = new DateOnly(2026, 5, 10), DueOnOrBefore = new DateOnly(2026, 5, 10), BankAccountCode = "1100" };
        PaymentRunProposal proposal = (await run.ProposeAsync(request)).Value;
        proposal.Lines.Single(l => l.SupplierId == certified.Id).Withholding.Should().Be(Math.Round(proposal.Lines.Single(l => l.SupplierId == certified.Id).ToPay * 0.10m, 2));

        long renewal = (await tax.RecordCertificateAsync(companyId, TradingLedger.AccountsPayable, "SC-CRT", "TC-78", new DateOnly(2026, 5, 5), new DateOnly(2026, 11, 30), null, CertificatePurpose.TaxClearance)).Value;
        renewal.Should().BePositive();
        (await run.ProposeAsync(request)).Value.Lines.Single(l => l.SupplierId == certified.Id).Withholding.Should().Be(0m);
        (await tax.ExpiringAsync(companyId, new DateOnly(2026, 11, 15))).Should().Contain(e => e.CertificateNumber == "TC-78" && e.Purpose == CertificatePurpose.TaxClearance);
    }
}
