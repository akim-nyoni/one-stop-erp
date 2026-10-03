using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers withholding in the supplier payment run: the proposal shows the tax that will be withheld from an uncleared
/// supplier, the run settles the invoice in full while the bank file carries only the net, and a cleared supplier is
/// paid in full.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PaymentRunWithholdingTests
{
    private readonly AccountingFixture _fixture;

    public PaymentRunWithholdingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_run_withholds_from_uncleared_suppliers_and_pays_the_net()
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
        Supplier uncleared = new(companyId, "PRW-U", "Builder Ltd", Scenario.Usd) { BankAccountNumber = "111" };
        Supplier cleared = new(companyId, "PRW-C", "Compliant Ltd", Scenario.Usd) { BankAccountNumber = "222", TaxClearanceExpiry = new DateOnly(2026, 12, 31) };
        db.Suppliers.AddRange(uncleared, cleared);
        await db.SaveChangesAsync();

        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = uncleared.Id, DocumentNumber = "PRW-I1", DocumentDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, NetAmount = 1_000m, ExpenseAccountCode = "6800" });
        await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = cleared.Id, DocumentNumber = "PRW-I2", DocumentDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, NetAmount = 400m, ExpenseAccountCode = "6800" });

        PaymentRunRequest request = new()
        {
            CompanyId = companyId, PaymentDate = new DateOnly(2026, 4, 1), DueOnOrBefore = new DateOnly(2026, 4, 1), BankAccountCode = "1100", WithholdingTaxCode = "WHT30",
        };
        IPaymentRunService run = scope.ServiceProvider.GetRequiredService<IPaymentRunService>();

        PaymentRunProposal proposal = (await run.ProposeAsync(request)).Value;
        proposal.Lines.Single(l => l.SupplierId == uncleared.Id).Should().BeEquivalentTo(new { ToPay = 1_000m, Withholding = 300m });
        proposal.Lines.Single(l => l.SupplierId == cleared.Id).Withholding.Should().Be(0m);

        PaymentRunResult result = (await run.RunAsync(request)).Value;
        result.Lines.Single(l => l.SupplierId == uncleared.Id).Should().BeEquivalentTo(new { Amount = 1_000m, Withheld = 300m, NetPaid = 700m });
        result.Lines.Single(l => l.SupplierId == cleared.Id).Should().BeEquivalentTo(new { Amount = 400m, Withheld = 0m });

        string eft = System.Text.Encoding.UTF8.GetString(result.EftFile);
        eft.Should().StartWith("HDR|20260401|1100|2|1100.00");
        eft.Split('\n').Single(l => l.Contains("PRW-U", StringComparison.Ordinal)).TrimEnd('\r').Should().EndWith("|USD|700.00");

        // Both invoices are settled in full; the 300 is owed to the revenue authority.
        AgeingReport creditors = await scope.ServiceProvider.GetRequiredService<ITradingReports>().AgedCreditorsAsync(companyId, new DateOnly(2026, 4, 1));
        creditors.Total.Should().Be(0m);
        (await scope.ServiceProvider.GetRequiredService<IWithholdingTaxService>().ReturnAsync(companyId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30))).Value.Unremitted.Should().Be(300m);
    }
}
