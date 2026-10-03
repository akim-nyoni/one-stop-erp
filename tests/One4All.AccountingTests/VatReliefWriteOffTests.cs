using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers bad-debt write-off with VAT relief: an invoice of 1,000 + 150 VAT is part-paid 460, leaving 690; relief is
/// refused until the debt has been overdue 180 days; then 90 of output VAT is reclaimed and only 600 goes to bad debt,
/// the invoice is settled, and output VAT falls to the 60 actually collected.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class VatReliefWriteOffTests
{
    private readonly AccountingFixture _fixture;

    public VatReliefWriteOffTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Writing_off_with_relief_reclaims_the_vat_share_of_the_debt()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "6950", "Bad Debts", AccountType.Expense));
        Customer customer = new(companyId, "VR-C", "Gone Away Ltd", Scenario.Usd) { PaymentTermsDays = 0 };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        long invoice = (await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "VR-I1", DocumentDate = new DateOnly(2026, 1, 2), Currency = Scenario.Usd,
            NetAmount = 1_000m, TaxAmount = 150m, TaxAccountCode = "2200", SalesAccountCode = "4000",
        })).Value.OpenItemId;
        long receipt = (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "VR-R1", ReceiptDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, Amount = 460m, BankAccountCode = "1100" })).Value.OpenItemId;
        (await scope.ServiceProvider.GetRequiredService<IAllocationService>().AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoice, receipt, 460m, new DateOnly(2026, 2, 1))).IsSuccess.Should().BeTrue();

        (await ar.WriteOffWithVatReliefAsync(companyId, invoice, new DateOnly(2026, 3, 1), "6950", "Customer in liquidation", 180)).Error.Code.Should().Be("Ar.VatReliefTooEarly");

        VatReliefWriteOff written = (await ar.WriteOffWithVatReliefAsync(companyId, invoice, new DateOnly(2026, 7, 6), "6950", "Customer in liquidation", 180)).Value;
        (written.WrittenOffToBadDebt, written.VatRelief).Should().Be((600m, 90m));
        (await db.OpenItems.AsNoTracking().SingleAsync(i => i.Id == invoice)).OutstandingTransaction.Should().Be(0m);

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 7, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "2200").Credit.Should().Be(60m);
        tb.Rows.Single(r => r.AccountCode == "6950").Debit.Should().Be(600m);
        tb.Rows.Should().NotContain(r => r.AccountCode == "1200" && (r.Debit != 0m || r.Credit != 0m));
    }
}
