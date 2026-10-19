using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Banking;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Banking;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;
using IAllocationService = One4All.Application.Abstractions.Trading.IAllocationService;

namespace One4All.AccountingTests;

/// <summary>A supplier payment that did not go through is reversed: cancelling its cheque puts the bank back and the invoice is owed again.</summary>
[Collection(AccountingCollection.Name)]
public sealed class SupplierPaymentReversalTests
{
    private readonly AccountingFixture _fixture;

    public SupplierPaymentReversalTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Cancelling_a_cheque_reverses_the_payment_it_made()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Supplier supplier = new(companyId, "SPR-S", "Supplier", Scenario.Usd);
        BankAccount bank = new(companyId, "SPR-B", "Main", Scenario.Usd, "1100");
        db.AddRange(supplier, bank);
        await db.SaveChangesAsync();
        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        long invoice = (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "SPR-INV", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 400m, ExpenseAccountCode = "6800",
        })).Value.OpenItemId;
        long payment = (await ap.PostPaymentAsync(new PaymentRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "SPR-PAY", PaymentDate = new DateOnly(2026, 3, 10), Currency = Scenario.Usd, Amount = 400m, BankAccountCode = "1100",
        })).Value.OpenItemId;
        (await scope.ServiceProvider.GetRequiredService<IAllocationService>().AllocateAsync(companyId, TradingLedger.AccountsPayable, payment, invoice, 400m, new DateOnly(2026, 3, 10))).IsSuccess
            .Should().BeTrue();
        IChequeService cheques = scope.ServiceProvider.GetRequiredService<IChequeService>();
        long cheque = (await cheques.IssueAsync(companyId, bank.Id, "000777", new DateOnly(2026, 3, 10), "Supplier", 400m, "SPR-PAY")).Value;
        decimal Bank() => db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId).SelectMany(j => j.Lines).Where(l => l.AccountCode == "1100").Sum(l => l.BaseDebit - l.BaseCredit);
        decimal before = Bank();

        (await cheques.CancelAsync(companyId, cheque, new DateOnly(2026, 3, 20), "Lost in the post")).IsSuccess.Should().BeTrue();
        Bank().Should().Be(before + 400m, "the money never left the bank");
        (await db.OpenItems.AsNoTracking().SingleAsync(o => o.Id == invoice)).OutstandingTransaction.Should().Be(400m, "the invoice is owed again");
        (await db.OpenItems.AsNoTracking().SingleAsync(o => o.Id == payment)).OutstandingTransaction.Should().Be(0m);
        (await ap.ReversePaymentAsync(companyId, "SPR-PAY", new DateOnly(2026, 3, 21), "Again")).Error.Code.Should().Be("Ap.PaymentReversed");
        (await ap.ReversePaymentAsync(companyId, "NOPE", new DateOnly(2026, 3, 21), "x")).Error.Code.Should().Be("Ap.PaymentNotFound");
    }
}
