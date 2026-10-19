using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>A payment run takes a supplier's settlement discount on invoices paid within the discount days, even before they are due.</summary>
[Collection(AccountingCollection.Name)]
public sealed class SupplierSettlementDiscountRunTests
{
    private readonly AccountingFixture _fixture;

    public SupplierSettlementDiscountRunTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Early_payment_takes_the_discount()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Supplier quick = new(companyId, "SSD-Q", "Discounting supplier", Scenario.Usd) { SettlementDiscountPercent = 2.5m, SettlementDiscountDays = 10, BankName = "Bank", BankAccountNumber = "1" };
        db.Suppliers.Add(quick);
        (await db.Companies.SingleAsync(c => c.Id == companyId)).SettlementDiscountReceivedAccountCode = "4950";
        await db.SaveChangesAsync();
        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        foreach ((string number, DateOnly date) in new[] { ("SSD-1", new DateOnly(2026, 3, 25)), ("SSD-OLD", new DateOnly(2026, 2, 1)) })
        {
            (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
            {
                CompanyId = companyId, SupplierId = quick.Id, DocumentNumber = number, DocumentDate = date, Currency = Scenario.Usd, NetAmount = 400m, ExpenseAccountCode = "6800",
            })).IsSuccess.Should().BeTrue();
        }

        // 1 April: SSD-1 (25 March) is within 10 days though not due until April 24 - paid at 390 with 10 discount; SSD-OLD is due and past its discount days - 400.
        PaymentRunResult run = (await scope.ServiceProvider.GetRequiredService<IPaymentRunService>().RunAsync(new PaymentRunRequest
        {
            CompanyId = companyId, PaymentDate = new DateOnly(2026, 4, 1), DueOnOrBefore = new DateOnly(2026, 4, 1), BankAccountCode = "1100",
        })).Value;
        PaymentRunLine line = run.Lines.Single(l => l.SupplierId == quick.Id);
        (line.Amount, line.DiscountTaken, line.InvoicesPaid).Should().Be((790m, 10m, 2));
        (await db.OpenItems.AsNoTracking().Where(i => i.CompanyId == companyId && i.TraderId == quick.Id && i.Ledger == TradingLedger.AccountsPayable).SumAsync(i => i.OutstandingTransaction))
            .Should().Be(0m, "both invoices are settled");
        (await db.OpenItems.AsNoTracking().SingleAsync(i => i.CompanyId == companyId && i.DocumentNumber == "SD-SSD-1")).OriginalTransaction.Should().Be(10m);
    }
}
