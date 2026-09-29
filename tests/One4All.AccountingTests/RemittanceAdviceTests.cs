using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers rendering a supplier remittance advice for a payment allocated against invoices.</summary>
[Collection(AccountingCollection.Name)]
public sealed class RemittanceAdviceTests
{
    private readonly AccountingFixture _fixture;

    public RemittanceAdviceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Remittance_lists_the_invoices_a_payment_settled()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier supplier = new(companyId, "SUP1", "Widget Wholesale", Scenario.Usd);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            supplierId = supplier.Id;
        }

        long paymentItemId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            var invoice = await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
            {
                CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "PINV-1", DocumentDate = new DateOnly(2026, 3, 1),
                Currency = Scenario.Usd, NetAmount = 250m, TaxAmount = 0m, ExpenseAccountCode = "6800",
            });
            var payment = await ap.PostPaymentAsync(new PaymentRequest
            {
                CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "PAY-1", PaymentDate = new DateOnly(2026, 3, 20),
                Currency = Scenario.Usd, Amount = 250m, BankAccountCode = "1100",
            });
            paymentItemId = payment.Value.OpenItemId;

            IAllocationService allocations = scope.ServiceProvider.GetRequiredService<IAllocationService>();
            // In AP the payment is the debit item and the supplier invoice the credit item.
            var alloc = await allocations.AllocateAsync(companyId, TradingLedger.AccountsPayable, payment.Value.OpenItemId, invoice.Value.OpenItemId, 250m, new DateOnly(2026, 3, 20));
            alloc.IsSuccess.Should().BeTrue(alloc.IsFailure ? alloc.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRemittanceService remittance = scope.ServiceProvider.GetRequiredService<IRemittanceService>();
            var result = await remittance.RenderRemittanceTextAsync(companyId, paymentItemId);

            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            string text = result.Value;
            text.Should().Contain("REMITTANCE ADVICE");
            text.Should().Contain("Widget Wholesale");
            text.Should().Contain("PAY-1");
            text.Should().Contain("PINV-1").And.Contain("250.00");
            text.Should().Contain("TOTAL PAID");

            (await remittance.RenderRemittanceTextAsync(companyId, 999_999)).Error.Code.Should().Be("Remittance.PaymentNotFound");
        }
    }
}
