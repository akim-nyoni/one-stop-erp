using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers dual authorisation of supplier bank details: a requested change leaves the details as they are and holds the
/// supplier out of the payment run; the requester cannot approve it and a second request is refused; once a different
/// user approves, the details change and the next run pays to the new account. A rejected change leaves them alone.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SupplierBankChangeTests
{
    private const long Clerk = 11;
    private const long Manager = 22;

    private readonly AccountingFixture _fixture;

    public SupplierBankChangeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Bank_details_change_only_with_a_second_users_approval()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Supplier supplier = new(companyId, "SBC-1", "Steady Supplies", Scenario.Usd) { BankAccountNumber = "111", BankName = "Old Bank" };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>().PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "SBC-I1", DocumentDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, NetAmount = 300m, ExpenseAccountCode = "6800",
        });

        ISupplierBankDetailsService bank = scope.ServiceProvider.GetRequiredService<ISupplierBankDetailsService>();
        long changeId = (await bank.RequestChangeAsync(companyId, supplier.Id, new SupplierBankDetails("Steady Supplies", "999", "New Bank"), Clerk)).Value;
        (await bank.RequestChangeAsync(companyId, supplier.Id, new SupplierBankDetails(null, "555", null), Clerk)).Error.Code.Should().Be("Ap.BankChangePending");
        (await bank.PendingAsync(companyId)).Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            ChangeId = changeId,
            Current = new SupplierBankDetails(null, "111", "Old Bank"),
            Requested = new SupplierBankDetails("Steady Supplies", "999", "New Bank"),
            RequestedByUserId = Clerk,
        });

        // Held out of the run while the change is pending.
        IPaymentRunService run = scope.ServiceProvider.GetRequiredService<IPaymentRunService>();
        PaymentRunRequest request = new() { CompanyId = companyId, PaymentDate = new DateOnly(2026, 4, 1), DueOnOrBefore = new DateOnly(2026, 4, 1), BankAccountCode = "1100" };
        (await run.ProposeAsync(request)).Value.Lines.Single().Should().BeEquivalentTo(new { ToPay = 0m, OnHold = true, BankChangePending = true });
        (await run.RunAsync(request)).Value.Lines.Should().BeEmpty();

        (await bank.ApproveAsync(companyId, changeId, Clerk)).Error.Code.Should().Be("Ap.SelfApproval");
        (await bank.ApproveAsync(companyId, changeId, Manager)).IsSuccess.Should().BeTrue();
        Supplier changed = await db.Suppliers.AsNoTracking().SingleAsync(s => s.Id == supplier.Id);
        (changed.BankAccountNumber, changed.BankName).Should().Be(("999", "New Bank"));

        PaymentRunResult paid = (await run.RunAsync(request)).Value;
        paid.Lines.Should().ContainSingle().Which.Amount.Should().Be(300m);
        System.Text.Encoding.UTF8.GetString(paid.EftFile).Should().Contain("|New Bank|999|USD|300.00");

        // A rejected change leaves the details alone; approving it afterwards is refused.
        long rejected = (await bank.RequestChangeAsync(companyId, supplier.Id, new SupplierBankDetails(null, "666", "Other Bank"), Clerk)).Value;
        (await bank.RejectAsync(companyId, rejected, Manager, " ")).Error.Code.Should().Be("Ap.ReasonRequired");
        (await bank.RejectAsync(companyId, rejected, Manager, "Could not confirm by phone")).IsSuccess.Should().BeTrue();
        (await bank.ApproveAsync(companyId, rejected, Manager)).Error.Code.Should().Be("Ap.BankChangeNotPending");
        (await db.Suppliers.AsNoTracking().SingleAsync(s => s.Id == supplier.Id)).BankAccountNumber.Should().Be("999");
        (await bank.PendingAsync(companyId)).Should().BeEmpty();
    }
}
