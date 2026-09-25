using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RemittanceAdviceTests
{
    private readonly AccountingFixture _fixture;

    public RemittanceAdviceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Remittance_advice_lists_the_invoices_a_payment_settled()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        long inv1Id;
        long inv2Id;
        long paymentId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier supplier = new(companyId, "RA-SUP", "Remittance Supplier", Scenario.Usd);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            supplierId = supplier.Id;

            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            inv1Id = (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "RA-P1", DocumentDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, NetAmount = 100m, ExpenseAccountCode = "5000" })).Value.OpenItemId;
            inv2Id = (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "RA-P2", DocumentDate = new DateOnly(2026, 2, 10), Currency = Scenario.Usd, NetAmount = 150m, ExpenseAccountCode = "5000" })).Value.OpenItemId;

            // One payment of 250 settling both invoices.
            paymentId = (await ap.PostPaymentAsync(new PaymentRequest { CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "RA-PAY1", PaymentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, Amount = 250m, BankAccountCode = "1100" })).Value.OpenItemId;

            IAllocationService alloc = scope.ServiceProvider.GetRequiredService<IAllocationService>();
            await alloc.AllocateAsync(companyId, TradingLedger.AccountsPayable, paymentId, inv1Id, 100m, new DateOnly(2026, 3, 1));
            await alloc.AllocateAsync(companyId, TradingLedger.AccountsPayable, paymentId, inv2Id, 150m, new DateOnly(2026, 3, 1));
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();

            (await reports.RemittanceAdviceAsync(companyId, inv1Id)).Should().BeNull(); // not a payment item

            RemittanceAdvice? advice = await reports.RemittanceAdviceAsync(companyId, paymentId);
            advice.Should().NotBeNull();
            advice!.SupplierCode.Should().Be("RA-SUP");
            advice.PaymentNumber.Should().Be("RA-PAY1");
            advice.Lines.Should().HaveCount(2);
            advice.Lines.Select(l => l.InvoiceNumber).Should().BeEquivalentTo(["RA-P1", "RA-P2"]);
            advice.TotalAllocated.Should().Be(250m);
        }
    }
}
