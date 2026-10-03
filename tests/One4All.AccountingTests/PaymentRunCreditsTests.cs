using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the payment run netting supplier credits: an unused credit note is set against the oldest invoice before
/// paying the remainder (no overpayment); a supplier whose payment on account covers everything due is settled with
/// nothing paid and left out of the EFT file; a supplier on hold is proposed with nothing to pay and skipped; the
/// proposal matches what the run then does; and a second run on the same day continues the payment numbering.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PaymentRunCreditsTests
{
    private readonly AccountingFixture _fixture;

    public PaymentRunCreditsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Credits_are_applied_before_paying_and_numbering_continues()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long withCreditNote;
        long onAccount;
        long held;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier a = new(companyId, "RUN-A", "Credit Note Supplier", Scenario.Usd) { BankName = "First Bank", BankAccountNumber = "111" };
            Supplier b = new(companyId, "RUN-B", "On Account Supplier", Scenario.Usd) { BankName = "First Bank", BankAccountNumber = "222" };
            Supplier c = new(companyId, "RUN-C", "Disputed Supplier", Scenario.Usd) { OnHold = true };
            db.Suppliers.AddRange(a, b, c);
            await db.SaveChangesAsync();
            (withCreditNote, onAccount, held) = (a.Id, b.Id, c.Id);

            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = a.Id, DocumentNumber = "A-1", DocumentDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, NetAmount = 100m, ExpenseAccountCode = "6800" });
            await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = a.Id, DocumentNumber = "A-2", DocumentDate = new DateOnly(2026, 2, 15), Currency = Scenario.Usd, NetAmount = 200m, ExpenseAccountCode = "6800" });
            (await ap.PostCreditNoteAsync(new PurchaseCreditNoteRequest { CompanyId = companyId, SupplierId = a.Id, DocumentNumber = "A-CN", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 50m, ExpenseAccountCode = "6800" })).IsSuccess.Should().BeTrue();

            await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = b.Id, DocumentNumber = "B-1", DocumentDate = new DateOnly(2026, 2, 10), Currency = Scenario.Usd, NetAmount = 80m, ExpenseAccountCode = "6800" });
            (await ap.PostPaymentAsync(new PaymentRequest { CompanyId = companyId, SupplierId = b.Id, DocumentNumber = "B-ADV", PaymentDate = new DateOnly(2026, 3, 5), Currency = Scenario.Usd, Amount = 100m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();
            await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = c.Id, DocumentNumber = "C-1", DocumentDate = new DateOnly(2026, 2, 5), Currency = Scenario.Usd, NetAmount = 70m, ExpenseAccountCode = "6800" });
        }

        DateOnly runDate = new(2026, 4, 1);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            // The proposal posts nothing and says exactly what the run will do.
            PaymentRunProposal proposal = (await scope.ServiceProvider.GetRequiredService<IPaymentRunService>().ProposeAsync(new PaymentRunRequest
            {
                CompanyId = companyId, PaymentDate = runDate, DueOnOrBefore = runDate, BankAccountCode = "1100",
            })).Value;
            proposal.Lines.Single(l => l.SupplierId == withCreditNote).Should().BeEquivalentTo(new { InvoicesDue = 2, AmountDue = 300m, CreditsAvailable = 50m, CreditsToApply = 50m, ToPay = 250m, OnHold = false });
            proposal.Lines.Single(l => l.SupplierId == onAccount).Should().BeEquivalentTo(new { AmountDue = 80m, CreditsAvailable = 100m, CreditsToApply = 80m, ToPay = 0m });
            proposal.Lines.Single(l => l.SupplierId == held).Should().BeEquivalentTo(new { AmountDue = 70m, ToPay = 0m, OnHold = true });
            proposal.PaymentCount.Should().Be(1);
            proposal.ToPayByCurrency.Should().BeEquivalentTo(new Dictionary<string, decimal> { ["USD"] = 250m });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            PaymentRunResult result = (await scope.ServiceProvider.GetRequiredService<IPaymentRunService>().RunAsync(new PaymentRunRequest
            {
                CompanyId = companyId, PaymentDate = runDate, DueOnOrBefore = runDate, BankAccountCode = "1100",
            })).Value;

            // A: 300 due less the 50 credit note → 250 paid, both invoices settled.
            result.Lines.Single(l => l.SupplierId == withCreditNote).Should().BeEquivalentTo(new
            {
                Amount = 250m, CreditsApplied = 50m, InvoicesPaid = 2, PaymentNumber = "PMT-20260401-0001",
            });

            // B: the 100 on account covers the 80 invoice → nothing paid, 20 left on account.
            result.Lines.Single(l => l.SupplierId == onAccount).Should().BeEquivalentTo(new { Amount = 0m, CreditsApplied = 80m, InvoicesPaid = 1, PaymentNumber = string.Empty });

            result.Lines.Should().NotContain(l => l.SupplierId == held);
            result.Total.Should().Be(250m);
            string eft = System.Text.Encoding.UTF8.GetString(result.EftFile);
            eft.Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(l => l.StartsWith("PMT|", StringComparison.Ordinal)).Should().Be(1);
            eft.Should().Contain("RUN-A").And.NotContain("RUN-B");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport creditors = await reports.AgedCreditorsAsync(companyId, runDate);
            creditors.Total.Should().Be(50m); // B's unused 20 on account against C's held 70

            // A later invoice paid in a second run the same day continues the day's numbering.
            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = withCreditNote, DocumentNumber = "A-3", DocumentDate = new DateOnly(2026, 3, 20), Currency = Scenario.Usd, NetAmount = 40m, ExpenseAccountCode = "6800" });
            PaymentRunResult second = (await scope.ServiceProvider.GetRequiredService<IPaymentRunService>().RunAsync(new PaymentRunRequest
            {
                CompanyId = companyId, PaymentDate = runDate, DueOnOrBefore = new DateOnly(2026, 12, 31), BankAccountCode = "1100", SupplierIds = [withCreditNote],
            })).Value;
            second.Lines.Should().ContainSingle().Which.PaymentNumber.Should().Be("PMT-20260401-0002");

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, runDate);
            tb.IsBalanced.Should().BeTrue();
        }
    }
}
