using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Security;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Audit finding 10: a single supplier payment a user keys meets the payment run's controls - not to a supplier on hold or with a bank-detail change
/// waiting, and, where the company requires payment approval, approved by a user other than whoever keys it. Payments the system posts from documents
/// controlled elsewhere (no user keying them) are not refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SupplierPaymentControlTests
{
    private readonly AccountingFixture _fixture;

    public SupplierPaymentControlTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_keyed_payment_meets_the_payment_run_controls()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        string tag = Guid.NewGuid().ToString("N")[..8];
        User payer = new($"spc.payer.{tag}", "Payer");
        User approver = new($"spc.approver.{tag}", "Approver");
        db.Users.AddRange(payer, approver);
        Supplier steady = new(companyId, "SPC-1", "Steady Supplies", Scenario.Usd);
        Supplier held = new(companyId, "SPC-2", "Disputed Supplies", Scenario.Usd) { OnHold = true };
        Supplier changing = new(companyId, "SPC-3", "Moving Bank Ltd", Scenario.Usd) { BankAccountNumber = "111" };
        db.Suppliers.AddRange(steady, held, changing);
        await db.SaveChangesAsync();
        db.Set<SupplierBankChange>().Add(new SupplierBankChange(companyId, changing.Id, "Moving Bank Ltd", "999", "New Bank", 999_961));
        await db.SaveChangesAsync();

        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        int n = 0;
        PaymentRequest Pay(Supplier s, long? enteredBy = null, long? approvedBy = null) => new()
        {
            CompanyId = companyId, SupplierId = s.Id, DocumentNumber = $"SPC-P{++n}", PaymentDate = new DateOnly(2026, 10, 1), Currency = Scenario.Usd, Amount = 50m,
            BankAccountCode = "1100", EnteredByUserId = enteredBy, ApprovedByUserId = approvedBy,
        };

        (await ap.PostPaymentAsync(Pay(held, payer.Id))).Error.Code.Should().Be("Ap.SupplierHeld");
        (await ap.PostPaymentAsync(Pay(changing, payer.Id))).Error.Code.Should().Be("Ap.BankChangePending");
        (await ap.PostPaymentAsync(Pay(steady, payer.Id))).IsSuccess.Should().BeTrue("no approval is required yet");

        // A payment no user keys (a bank statement line already paid, a contra) is recorded as before.
        (await ap.PostPaymentAsync(Pay(held))).IsSuccess.Should().BeTrue();

        (await db.Companies.SingleAsync(c => c.Id == companyId)).PaymentRunApprovalRequired = true;
        await db.SaveChangesAsync();
        (await ap.PostPaymentAsync(Pay(steady, payer.Id))).Error.Code.Should().Be("Ap.PaymentApprovalRequired");
        (await ap.PostPaymentAsync(Pay(steady, payer.Id, payer.Id))).Error.Code.Should().Be("Ap.PaymentSelfApproval");
        (await ap.PostPaymentAsync(Pay(held, payer.Id, approver.Id))).Error.Code.Should().Be("Ap.SupplierHeld", "approval does not release a held supplier");
        TradingPostingResult paid = (await ap.PostPaymentAsync(Pay(steady, payer.Id, approver.Id))).Value;
        paid.BaseAmount.Should().Be(50m);
    }
}
