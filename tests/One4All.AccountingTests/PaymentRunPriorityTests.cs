using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers paying suppliers by priority when funds are short, mobile-money runs to wallets, and wallets changed only through an approved change.</summary>
[Collection(AccountingCollection.Name)]
public sealed class PaymentRunPriorityTests
{
    private readonly AccountingFixture _fixture;

    public PaymentRunPriorityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Short_funds_pay_by_priority_and_wallets_are_paid_by_mobile_money()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Supplier urgent = new(companyId, "PRI-A", "Fuel supplier", Scenario.Usd) { PaymentPriority = 1 };
        Supplier normal = new(companyId, "PRI-B", "Stationer", Scenario.Usd);
        Supplier wallet = new(companyId, "PRI-C", "Farmer", Scenario.Usd) { MobileMoneyNumber = "0772123456" };
        db.Suppliers.AddRange(urgent, normal, wallet);
        await db.SaveChangesAsync();
        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        foreach ((Supplier s, string number, decimal amount) in new[] { (normal, "PRI-I1", 300m), (urgent, "PRI-I2", 500m), (wallet, "PRI-I3", 100m) })
        {
            (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
            {
                CompanyId = companyId, SupplierId = s.Id, DocumentNumber = number, DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = amount, ExpenseAccountCode = "6800",
            })).IsSuccess.Should().BeTrue();
        }

        IPaymentRunService runs = scope.ServiceProvider.GetRequiredService<IPaymentRunService>();
        PaymentRunRequest Request(decimal? funds, PaymentRunMethod method, IReadOnlyList<long>? only = null) => new()
        {
            CompanyId = companyId, PaymentDate = new DateOnly(2026, 3, 31), DueOnOrBefore = new DateOnly(2026, 6, 30), BankAccountCode = "1100", Currency = "USD", FundsAvailable = funds,
            Method = method, SupplierIds = only ?? [],
        };

        (await runs.ProposeAsync(Request(-1m, PaymentRunMethod.Eft))).Error.Code.Should().Be("Ap.FundsAvailable");
        PaymentRunProposal proposal = (await runs.ProposeAsync(Request(650m, PaymentRunMethod.Eft))).Value;
        proposal.Lines.Where(l => l.SupplierCode.StartsWith("PRI-", StringComparison.Ordinal)).Select(l => (l.SupplierCode, l.ToPay, l.Deferred)).Should()
            .Equal(("PRI-A", 500m, 0m), ("PRI-B", 150m, 150m), ("PRI-C", 0m, 100m));

        // Mobile money: a supplier with no wallet stops the run; one with a wallet gets a payout line.
        (await runs.RunAsync(Request(null, PaymentRunMethod.MobileMoney, [normal.Id]))).Error.Code.Should().Be("Ap.NoWallet");
        PaymentRunResult paid = (await runs.RunAsync(Request(null, PaymentRunMethod.MobileMoney, [wallet.Id]))).Value;
        string file = System.Text.Encoding.UTF8.GetString(paid.EftFile);
        file.Should().StartWith("HDR-MM|20260331|1|100.00").And.Contain("|PRI-C|Farmer|0772123456|USD|100.00");

        // A wallet changes only through an approved change, which keeps the bank account.
        ISupplierBankDetailsService bank = scope.ServiceProvider.GetRequiredService<ISupplierBankDetailsService>();
        long change = (await bank.RequestChangeAsync(companyId, normal.Id, new SupplierBankDetails(null, null, null) { MobileMoneyNumber = "0712000111" }, 81)).Value;
        (await bank.ApproveAsync(companyId, change, 82)).IsSuccess.Should().BeTrue();
        (await db.Suppliers.AsNoTracking().SingleAsync(s => s.Id == normal.Id)).MobileMoneyNumber.Should().Be("0712000111");
    }
}
