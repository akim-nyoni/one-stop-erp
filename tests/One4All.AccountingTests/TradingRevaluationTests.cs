using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers period-end revaluation of foreign-currency receivables and payables: EUR items booked at 1.10 are
/// retranslated at the 1.20 closing rate (only what is still outstanding), the unrealised gain on AR and loss on AP
/// are posted and reversed the next day, a repeat run posts nothing, a date without a closing rate is refused, and
/// the control reconciliation shows the revaluation as the reconciling item while it is booked.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TradingRevaluationTests
{
    private readonly AccountingFixture _fixture;

    public TradingRevaluationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Foreign_balances_are_revalued_at_the_closing_rate_and_reversed_next_day()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long invoiceId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "EU-C", "Euro Buyer", Scenario.Eur);
            Supplier supplier = new(companyId, "EU-S", "Euro Supplier", Scenario.Eur);
            db.AddRange(customer, supplier);
            await db.SaveChangesAsync();

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            IAllocationService alloc = scope.ServiceProvider.GetRequiredService<IAllocationService>();

            // EUR 1,000 invoiced at 1.10 (USD 1,100); EUR 400 received and allocated at the same rate.
            invoiceId = (await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "EU-I1", DocumentDate = new DateOnly(2026, 3, 10), Currency = Scenario.Eur, NetAmount = 1_000m, SalesAccountCode = "4000" })).Value.OpenItemId;
            long receipt = (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "EU-R1", ReceiptDate = new DateOnly(2026, 5, 5), Currency = Scenario.Eur, Amount = 400m, BankAccountCode = "1150" })).Value.OpenItemId;
            (await alloc.AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoiceId, receipt, 400m, new DateOnly(2026, 5, 5))).IsSuccess.Should().BeTrue();

            // EUR 500 owed to the supplier, booked at USD 550.
            (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "EU-P1", DocumentDate = new DateOnly(2026, 3, 12), Currency = Scenario.Eur, NetAmount = 500m, ExpenseAccountCode = "6800" })).IsSuccess.Should().BeTrue();
        }

        DateOnly june30 = new(2026, 6, 30);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingRevaluationService reval = scope.ServiceProvider.GetRequiredService<ITradingRevaluationService>();

            // No closing rate is in force at the end of March.
            (await reval.RevalueAsync(companyId, TradingLedger.AccountsReceivable, new DateOnly(2026, 3, 31))).Error.Code.Should().Be("Reval.NoClosingRate");

            // AR: EUR 600 outstanding, booked at 660, worth 720 → unrealised gain 60.
            Result<TradingRevaluationResult> arRun = await reval.RevalueAsync(companyId, TradingLedger.AccountsReceivable, june30);
            arRun.IsSuccess.Should().BeTrue(arRun.IsFailure ? arRun.Error.ToString() : null);
            arRun.Value.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new
            {
                OpenItemId = invoiceId, Currency = "EUR", OutstandingForeign = 600m, BookedBase = 660m, ClosingRate = 1.20m, RevaluedBase = 720m, Difference = 60m,
            });
            arRun.Value.TargetRevaluation.Should().Be(60m);
            arRun.Value.Posted.Should().Be(60m);
            arRun.Value.JournalNumber.Should().NotBeNull();
            arRun.Value.ReversalJournalNumber.Should().NotBeNull();

            // AP: EUR 500 booked at 550, now 600 → the liability grows by 50, an unrealised loss.
            Result<TradingRevaluationResult> apRun = await reval.RevalueAsync(companyId, TradingLedger.AccountsPayable, june30);
            apRun.Value.TargetRevaluation.Should().Be(50m);

            // Repeating the AR run finds the 60 already booked and posts nothing.
            TradingRevaluationResult again = (await reval.RevalueAsync(companyId, TradingLedger.AccountsReceivable, june30)).Value;
            again.AlreadyBooked.Should().Be(60m);
            again.Posted.Should().Be(0m);
            again.JournalNumber.Should().BeNull();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();

            // At the period end the control carries the revaluation; it reconciles as a named item.
            ControlReconciliationReport atEnd = await gl.ControlReconciliationAsync(companyId, june30);
            Row(atEnd, "Accounts Receivable").Should().BeEquivalentTo(new { GlBalance = 720m, SubLedgerBalance = 660m, Revaluation = 60m, Difference = 0m });
            Row(atEnd, "Accounts Payable").Should().BeEquivalentTo(new { GlBalance = 600m, SubLedgerBalance = 550m, Revaluation = 50m, Difference = 0m });

            // The next day it has been reversed.
            ControlReconciliationReport nextDay = await gl.ControlReconciliationAsync(companyId, june30.AddDays(1));
            Row(nextDay, "Accounts Receivable").Should().BeEquivalentTo(new { GlBalance = 660m, SubLedgerBalance = 660m, Revaluation = 0m, Difference = 0m });

            ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], june30)).Value;
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Single(r => r.AccountCode == "7100").Credit.Should().Be(60m);
            tb.Rows.Single(r => r.AccountCode == "7200").Debit.Should().Be(50m);

            // The open items themselves are untouched, so check data stays clean.
            LedgerIntegrityReport check = (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value;
            check.Issues.Should().BeEmpty();
        }
    }

    private static ControlReconciliationRow Row(ControlReconciliationReport report, string control) => report.Rows.Single(r => r.Control == control);
}
