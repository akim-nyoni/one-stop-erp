using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers withholding tax on supplier payments (brief §3.5): paying 1,000 to a supplier without tax clearance withholds
/// 30% - the invoice is settled in full, the bank pays 700 and 300 is owed as WHT; a cleared supplier is paid in full;
/// the period's return lists the certificate, remitting pays the 300 over (and a second remittance finds nothing), and
/// the certificate text shows gross, withheld and net.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class WithholdingTaxTests
{
    private readonly AccountingFixture _fixture;

    public WithholdingTaxTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Nothing_is_withheld_where_no_cash_leaves()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "2495", "Withholding Tax Payable", AccountType.Liability));
        TaxCode wht = new(companyId, "WHT30", "Withholding 30%", TaxType.Withholding) { OutputAccountCode = "2495", IsActive = true };
        wht.AddRate(new DateOnly(2026, 1, 1), 30m);
        db.TaxCodes.Add(wht);
        Supplier uncleared = new(companyId, "WHT-N", "Contra Partner Ltd", Scenario.Usd);
        db.Suppliers.Add(uncleared);
        await db.SaveChangesAsync();

        // A contra or a creditor write-back moves no money to the supplier: even an uncleared supplier has nothing withheld (found with sixteenth audit #26).
        (await scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>().PostPaymentAsync(new PaymentRequest
        {
            CompanyId = companyId, SupplierId = uncleared.Id, DocumentNumber = "WHT-NC", PaymentDate = new DateOnly(2026, 3, 20), Currency = Scenario.Usd, Amount = 2_000m,
            BankAccountCode = "1100", WithholdingTaxCode = "WHT30", NoCashPaid = true,
        })).Value.WithheldBase.Should().Be(0m);
    }

    [SkippableFact]
    public async Task Payments_to_uncleared_suppliers_withhold_tax_that_is_then_remitted()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "2495", "Withholding Tax Payable", AccountType.Liability));
        TaxCode wht = new(companyId, "WHT30", "Withholding 30%", TaxType.Withholding) { OutputAccountCode = "2495", IsActive = true };
        wht.AddRate(new DateOnly(2026, 1, 1), 30m);
        db.TaxCodes.Add(wht);
        Supplier uncleared = new(companyId, "WHT-U", "Builder Ltd", Scenario.Usd);
        Supplier cleared = new(companyId, "WHT-C", "Compliant Ltd", Scenario.Usd) { TaxClearanceExpiry = new DateOnly(2026, 12, 31) };
        db.Suppliers.AddRange(uncleared, cleared);
        await db.SaveChangesAsync();

        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        IAllocationService alloc = scope.ServiceProvider.GetRequiredService<IAllocationService>();
        DateOnly paid = new(2026, 3, 20);
        long invoice = (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = uncleared.Id, DocumentNumber = "WHT-I1", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 1_000m, ExpenseAccountCode = "6800" })).Value.OpenItemId;

        Result<TradingPostingResult> payment = await ap.PostPaymentAsync(new PaymentRequest
        {
            CompanyId = companyId, SupplierId = uncleared.Id, DocumentNumber = "WHT-P1", PaymentDate = paid, Currency = Scenario.Usd, Amount = 1_000m, BankAccountCode = "1100", WithholdingTaxCode = "WHT30",
        });
        payment.IsSuccess.Should().BeTrue(payment.IsFailure ? payment.Error.ToString() : null);
        payment.Value.WithheldBase.Should().Be(300m);
        (await alloc.AllocateAsync(companyId, TradingLedger.AccountsPayable, payment.Value.OpenItemId, invoice, 1_000m, paid)).IsSuccess.Should().BeTrue();

        // The cleared supplier is paid in full even when the code is given.
        (await ap.PostPaymentAsync(new PaymentRequest
        {
            CompanyId = companyId, SupplierId = cleared.Id, DocumentNumber = "WHT-P2", PaymentDate = paid, Currency = Scenario.Usd, Amount = 500m, BankAccountCode = "1100", WithholdingTaxCode = "WHT30",
        })).Value.WithheldBase.Should().Be(0m);

        IWithholdingTaxService taxes = scope.ServiceProvider.GetRequiredService<IWithholdingTaxService>();
        WithholdingReturn march = (await taxes.ReturnAsync(companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31))).Value;
        march.Lines.Should().ContainSingle().Which.Should().BeEquivalentTo(new { SupplierCode = "WHT-U", Gross = 1_000m, Withheld = 300m, RemittedOn = (DateOnly?)null });
        march.Unremitted.Should().Be(300m);

        string certificate = (await taxes.CertificateTextAsync(companyId, march.Lines[0].CertificateId)).Value;
        certificate.Should().Contain("WITHHOLDING TAX CERTIFICATE").And.Contain("700.00");

        WithholdingRemittance remitted = (await taxes.RemitAsync(companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 10), "1100")).Value;
        remitted.Should().BeEquivalentTo(new { Certificates = 1, Amount = 300m });
        (await taxes.RemitAsync(companyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 10), "1100")).Value.JournalNumber.Should().BeNull();

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 4, 30))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Should().NotContain(r => r.AccountCode == "2495");               // paid over
        tb.Rows.Single(r => r.AccountCode == "1100").Credit.Should().Be(1_500m);  // 700 + 500 + 300
        tb.Rows.Single(r => r.AccountCode == "2100").Debit.Should().Be(500m);    // the invoice is settled in full; the 500 is on account
    }

    [SkippableFact]
    public async Task Withholding_starts_with_the_payment_that_brings_the_years_total_to_the_threshold()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "2495", "Withholding Tax Payable", AccountType.Liability));
        TaxCode wht = new(companyId, "WHT30", "Withholding 30%", TaxType.Withholding) { OutputAccountCode = "2495", IsActive = true };
        wht.AddRate(new DateOnly(2026, 1, 1), 30m);
        db.TaxCodes.Add(wht);
        Supplier uncleared = new(companyId, "WHT-T", "Small Trader", Scenario.Usd);
        db.Suppliers.Add(uncleared);
        Core.System.Company company = await db.Companies.FindAsync(companyId) ?? throw new InvalidOperationException("No company.");
        company.WithholdingWithoutClearanceTaxCode = "WHT30";
        await db.SaveChangesAsync();
        company.WithholdingThreshold.Should().Be(1_000m, "the statutory US$1,000 a year is the default");

        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        async Task<decimal> Pay(string number, decimal amount, DateOnly date) => (await ap.PostPaymentAsync(new PaymentRequest
        {
            CompanyId = companyId, SupplierId = uncleared.Id, DocumentNumber = number, PaymentDate = date, Currency = Scenario.Usd, Amount = amount, BankAccountCode = "1100",
        })).Value.WithheldBase;

        // 400 and 500 in the year: 900 in all, under the threshold - nothing withheld.
        (await Pay("WHT-T1", 400m, new DateOnly(2026, 1, 10))).Should().Be(0m);
        (await Pay("WHT-T2", 500m, new DateOnly(2026, 2, 10))).Should().Be(0m);

        // 200 more brings the year to 1,100: that payment is withheld in full (30% of 200), and so is every later one that year.
        (await Pay("WHT-T3", 200m, new DateOnly(2026, 3, 10))).Should().Be(60m);
        (await Pay("WHT-T4", 100m, new DateOnly(2026, 4, 10))).Should().Be(30m);

        // A single payment of the threshold or more is withheld on its own.
        Supplier other = new(companyId, "WHT-T2", "Large Trader", Scenario.Usd);
        db.Suppliers.Add(other);
        await db.SaveChangesAsync();
        (await ap.PostPaymentAsync(new PaymentRequest
        {
            CompanyId = companyId, SupplierId = other.Id, DocumentNumber = "WHT-T5", PaymentDate = new DateOnly(2026, 4, 10), Currency = Scenario.Usd, Amount = 1_000m, BankAccountCode = "1100",
        })).Value.WithheldBase.Should().Be(300m);
    }
}
