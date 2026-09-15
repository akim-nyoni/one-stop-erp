using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class TradingTests
{
    private readonly AccountingFixture _fixture;

    public TradingTests(AccountingFixture fixture) => _fixture = fixture;

    private static async Task<(long companyId, long customerId)> SeedCustomerAsync(ServiceProvider provider, CurrencyCode currency)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "CUST1", "Acme Buyers", currency) { PaymentTermsDays = 30 };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return (companyId, customer.Id);
    }

    [SkippableFact]
    public async Task Ar_invoice_receipt_allocate_settles_and_ages()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        (long companyId, long customerId) = await SeedCustomerAsync(provider, Scenario.Usd);

        long invoiceId;
        long receiptId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();

            var invoice = await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "INV-1", DocumentDate = new DateOnly(2026, 3, 1),
                Currency = Scenario.Usd, NetAmount = 100m, TaxAmount = 15m, SalesAccountCode = "4000", TaxAccountCode = "2200",
            });
            invoice.IsSuccess.Should().BeTrue(invoice.IsFailure ? invoice.Error.ToString() : null);
            invoiceId = invoice.Value.OpenItemId;

            var receipt = await ar.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "RCT-1", ReceiptDate = new DateOnly(2026, 3, 20),
                Currency = Scenario.Usd, Amount = 115m, BankAccountCode = "1100",
            });
            receipt.IsSuccess.Should().BeTrue(receipt.IsFailure ? receipt.Error.ToString() : null);
            receiptId = receipt.Value.OpenItemId;
        }

        // Before allocation the debtor is 115 as at 2026-03-31.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport aged = await reports.AgedDebtorsAsync(companyId, new DateOnly(2026, 3, 31));
            aged.Total.Should().Be(0m); // invoice +115, receipt -115 net to zero even before allocation
        }

        // Allocate the receipt against the invoice: same currency, no realised FX.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IAllocationService alloc = scope.ServiceProvider.GetRequiredService<IAllocationService>();
            var result = await alloc.AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoiceId, receiptId, 115m, new DateOnly(2026, 3, 20));
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            result.Value.RealisedFxBase.Should().Be(0m);
        }

        // Both items settled; statement balance is zero.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            OpenItem invoice = db.OpenItems.Find(invoiceId)!;
            invoice.IsSettled.Should().BeTrue();

            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            TraderStatement? statement = await reports.CustomerStatementAsync(companyId, customerId);
            statement!.Balance.Should().Be(0m);
        }
    }

    [SkippableFact]
    public async Task Ar_allocation_across_rates_realises_fx_gain()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        (long companyId, long customerId) = await SeedCustomerAsync(provider, Scenario.Eur);

        long invoiceId;
        long receiptId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();

            // Invoice 100 EUR at the daily rate 1.10 -> base 110.
            var invoice = await ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "INV-2", DocumentDate = new DateOnly(2026, 3, 1),
                Currency = Scenario.Eur, NetAmount = 100m, TaxAmount = 0m, SalesAccountCode = "4000",
            });
            invoiceId = invoice.Value.OpenItemId;
            invoice.Value.BaseAmount.Should().Be(110m);

            // Receipt 100 EUR at a stronger rate 1.20 -> base 120.
            var receipt = await ar.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "RCT-2", ReceiptDate = new DateOnly(2026, 4, 1),
                Currency = Scenario.Eur, Amount = 100m, BankAccountCode = "1150", ExchangeRate = 1.20m,
            });
            receiptId = receipt.Value.OpenItemId;
            receipt.Value.BaseAmount.Should().Be(120m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IAllocationService alloc = scope.ServiceProvider.GetRequiredService<IAllocationService>();
            var result = await alloc.AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoiceId, receiptId, 100m, new DateOnly(2026, 4, 1));
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            result.Value.RealisedFxBase.Should().Be(10m); // received 120 base for a 110 base invoice = 10 gain
            result.Value.FxJournalNumber.Should().NotBeNull();
        }
    }

    [SkippableFact]
    public async Task Ap_invoice_and_payment_post_and_age()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier supplier = new(companyId, "SUP1", "Widget Supplies", Scenario.Usd);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            supplierId = supplier.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            var invoice = await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
            {
                CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "PINV-1", DocumentDate = new DateOnly(2026, 3, 5),
                Currency = Scenario.Usd, NetAmount = 200m, TaxAmount = 30m, ExpenseAccountCode = "5000", TaxAccountCode = "2200",
            });
            invoice.IsSuccess.Should().BeTrue(invoice.IsFailure ? invoice.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();
            AgeingReport creditors = await reports.AgedCreditorsAsync(companyId, new DateOnly(2026, 3, 31));
            creditors.Total.Should().Be(230m); // 200 + 30 owed to the supplier
        }
    }
}
