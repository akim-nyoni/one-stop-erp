using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the IFRS 9 allowance end to end: at 30 April a 119-day debt (1,000 at 50%), a 10-day debt (400 at 3%) and a
/// not-yet-due invoice (200 at 1%) call for 514, while a debt covered by an unallocated receipt needs nothing; posting
/// books 514, a repeat posts nothing, and once the old debt is paid the May run releases the allowance to 46.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ExpectedCreditLossAccountingTests
{
    private readonly AccountingFixture _fixture;

    public ExpectedCreditLossAccountingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_allowance_follows_the_matrix_and_is_released_on_collection()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Accounts.Add(new LedgerAccount(companyId, "1290", "Allowance for Credit Losses", AccountType.Asset));
        db.Accounts.Add(new LedgerAccount(companyId, "6950", "Credit Loss Expense", AccountType.Expense));
        Customer old = new(companyId, "ECL-OLD", "Old Debt", Scenario.Usd) { PaymentTermsDays = 0 };
        Customer recent = new(companyId, "ECL-NEW", "Recent", Scenario.Usd) { PaymentTermsDays = 0 };
        Customer terms = new(companyId, "ECL-TRM", "On Terms", Scenario.Usd) { PaymentTermsDays = 30 };
        Customer paid = new(companyId, "ECL-PAID", "Paid Up", Scenario.Usd) { PaymentTermsDays = 0 };
        db.Customers.AddRange(old, recent, terms, paid);
        await db.SaveChangesAsync();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        long oldInvoice = (await Invoice(ar, companyId, old.Id, "ECL-I1", new DateOnly(2026, 1, 1), 1_000m)).OpenItemId;
        await Invoice(ar, companyId, recent.Id, "ECL-I2", new DateOnly(2026, 4, 20), 400m);
        await Invoice(ar, companyId, terms.Id, "ECL-I3", new DateOnly(2026, 4, 15), 200m);
        await Invoice(ar, companyId, paid.Id, "ECL-I4", new DateOnly(2026, 3, 1), 300m);
        await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = paid.Id, DocumentNumber = "ECL-R4", ReceiptDate = new DateOnly(2026, 4, 1), Currency = Scenario.Usd, Amount = 300m, BankAccountCode = "1100" });

        IExpectedCreditLossService ecl = scope.ServiceProvider.GetRequiredService<IExpectedCreditLossService>();
        DateOnly april = new(2026, 4, 30);
        EclReport report = (await ecl.AssessAsync(companyId, april, EclMatrix.Default, "1290")).Value;
        report.TotalExposure.Should().Be(1_600m);
        report.TargetAllowance.Should().Be(514m);
        report.Bands.Single(b => b.Band.FromDaysPastDue == 91).Exposure.Should().Be(1_000m);

        (await ecl.PostAsync(companyId, april, EclMatrix.Default, "1290", "6950")).Value.JournalNumber.Should().NotBeNull();
        (await ecl.PostAsync(companyId, april, EclMatrix.Default, "1290", "6950")).Value.JournalNumber.Should().BeNull();

        // The old debt is collected in May: the allowance falls to 400 × 10% + 200 × 3% = 46.
        long receipt = (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = old.Id, DocumentNumber = "ECL-R1", ReceiptDate = new DateOnly(2026, 5, 10), Currency = Scenario.Usd, Amount = 1_000m, BankAccountCode = "1100" })).Value.OpenItemId;
        (await scope.ServiceProvider.GetRequiredService<IAllocationService>().AllocateAsync(companyId, TradingLedger.AccountsReceivable, oldInvoice, receipt, 1_000m, new DateOnly(2026, 5, 10))).IsSuccess.Should().BeTrue();
        EclPostingResult may = (await ecl.PostAsync(companyId, new DateOnly(2026, 5, 31), EclMatrix.Default, "1290", "6950")).Value;
        may.Report.TargetAllowance.Should().Be(46m);
        may.Report.Adjustment.Should().Be(-468m);

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 5, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "1290").Credit.Should().Be(46m);
        tb.Rows.Single(r => r.AccountCode == "6950").Debit.Should().Be(46m);
    }

    private static async Task<TradingPostingResult> Invoice(ISalesLedgerService ar, long companyId, long customerId, string number, DateOnly date, decimal amount)
        => (await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = number, DocumentDate = date, Currency = Scenario.Usd, NetAmount = amount, SalesAccountCode = "4000" })).Value;
}
