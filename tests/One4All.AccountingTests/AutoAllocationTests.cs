using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers auto-allocation through the real allocation service: AR receipts and a credit note settle invoices
/// exact-first then oldest-first, AP payments settle supplier invoices, a ledger-wide run touches only traders
/// with both sides, and a re-run does nothing.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class AutoAllocationTests
{
    private readonly AccountingFixture _fixture;

    public AutoAllocationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Auto_allocation_settles_receivables_and_payables()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long idleCustomerId;
        long supplierId;
        long janInvoice;
        long febInvoice;
        long marInvoice;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "AA-C", "Busy Buyer", Scenario.Usd);
            Customer idle = new(companyId, "AA-IDLE", "Invoice Only", Scenario.Usd);
            Supplier supplier = new(companyId, "AA-S", "Supplier", Scenario.Usd);
            db.Customers.AddRange(customer, idle);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            idleCustomerId = idle.Id;
            supplierId = supplier.Id;

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            janInvoice = (await ar.PostInvoiceAsync(Invoice(companyId, customerId, "AA-I1", new DateOnly(2026, 1, 10), 100m))).Value.OpenItemId;
            febInvoice = (await ar.PostInvoiceAsync(Invoice(companyId, customerId, "AA-I2", new DateOnly(2026, 2, 10), 250m))).Value.OpenItemId;
            marInvoice = (await ar.PostInvoiceAsync(Invoice(companyId, customerId, "AA-I3", new DateOnly(2026, 3, 10), 400m))).Value.OpenItemId;
            (await ar.PostReceiptAsync(Receipt(companyId, customerId, "AA-R1", new DateOnly(2026, 3, 15), 250m))).IsSuccess.Should().BeTrue();
            (await ar.PostReceiptAsync(Receipt(companyId, customerId, "AA-R2", new DateOnly(2026, 3, 20), 300m))).IsSuccess.Should().BeTrue();
            (await ar.PostCreditNoteAsync(new SalesCreditNoteRequest
            {
                CompanyId = companyId, CustomerId = customerId, DocumentNumber = "AA-CN1", DocumentDate = new DateOnly(2026, 3, 25), Currency = Scenario.Usd,
                NetAmount = 50m, SalesAccountCode = "4000",
            })).IsSuccess.Should().BeTrue();
            (await ar.PostInvoiceAsync(Invoice(companyId, idleCustomerId, "AA-X1", new DateOnly(2026, 3, 1), 70m))).IsSuccess.Should().BeTrue();

            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "AA-P1", DocumentDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, NetAmount = 300m, ExpenseAccountCode = "5000" })).IsSuccess.Should().BeTrue();
            (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "AA-P2", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 500m, ExpenseAccountCode = "5000" })).IsSuccess.Should().BeTrue();
            (await ap.PostPaymentAsync(new PaymentRequest { CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "AA-PAY", PaymentDate = new DateOnly(2026, 3, 30), Currency = Scenario.Usd, Amount = 500m, BankAccountCode = "1100" })).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IAutoAllocationService auto = scope.ServiceProvider.GetRequiredService<IAutoAllocationService>();

            // Customer: R1 250 → I2 exactly; R2 300 → I1 100 + I3 200; CN 50 → I3 → I3 left with 150.
            var customer = await auto.AllocateTraderAsync(companyId, TradingLedger.AccountsReceivable, customerId, new DateOnly(2026, 3, 31));
            customer.IsSuccess.Should().BeTrue(customer.IsFailure ? customer.Error.ToString() : null);
            customer.Value.Allocations.Should().Be(4);
            customer.Value.ExactMatches.Should().Be(1);
            customer.Value.AmountAllocated.Should().Be(600m);
            customer.Value.Failures.Should().BeEmpty();

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await Outstanding(db, janInvoice)).Should().Be(0m);
            (await Outstanding(db, febInvoice)).Should().Be(0m);
            (await Outstanding(db, marInvoice)).Should().Be(150m);
            (await db.OpenItems.Where(i => i.TraderId == customerId && i.Ledger == TradingLedger.AccountsReceivable && i.Sign == LedgerSign.Credit).SumAsync(i => i.OutstandingTransaction)).Should().Be(0m);

            // Re-running finds nothing more to do.
            (await auto.AllocateTraderAsync(companyId, TradingLedger.AccountsReceivable, customerId, new DateOnly(2026, 3, 31))).Value.Allocations.Should().Be(0);

            // Ledger-wide AR run: the invoice-only customer has nothing to allocate, so no traders are processed.
            AutoAllocationResult arRun = await auto.AllocateLedgerAsync(companyId, TradingLedger.AccountsReceivable, new DateOnly(2026, 3, 31));
            arRun.TradersProcessed.Should().Be(0);

            // Ledger-wide AP run: the 500 payment settles the 500 invoice exactly, leaving the older 300 open.
            AutoAllocationResult apRun = await auto.AllocateLedgerAsync(companyId, TradingLedger.AccountsPayable, new DateOnly(2026, 3, 31));
            apRun.TradersProcessed.Should().BeGreaterThanOrEqualTo(1);
            (await db.OpenItems.Where(i => i.TraderId == supplierId && i.Ledger == TradingLedger.AccountsPayable && i.DocumentNumber == "AA-P2").Select(i => i.OutstandingTransaction).SingleAsync()).Should().Be(0m);
            (await db.OpenItems.Where(i => i.TraderId == supplierId && i.Ledger == TradingLedger.AccountsPayable && i.DocumentNumber == "AA-P1").Select(i => i.OutstandingTransaction).SingleAsync()).Should().Be(300m);

            (await auto.AllocateTraderAsync(companyId, TradingLedger.AccountsReceivable, 999_999, new DateOnly(2026, 3, 31))).Error.Code.Should().Be("Ar.CustomerNotFound");
        }
    }

    private static Task<decimal> Outstanding(One4AllDbContext db, long itemId) =>
        db.OpenItems.Where(i => i.Id == itemId).Select(i => i.OutstandingTransaction).SingleAsync();

    private static SalesInvoiceRequest Invoice(long companyId, long customerId, string number, DateOnly date, decimal net) => new()
    {
        CompanyId = companyId, CustomerId = customerId, DocumentNumber = number, DocumentDate = date,
        Currency = Scenario.Usd, NetAmount = net, TaxAmount = 0m, SalesAccountCode = "4000",
    };

    private static ReceiptRequest Receipt(long companyId, long customerId, string number, DateOnly date, decimal amount) => new()
    {
        CompanyId = companyId, CustomerId = customerId, DocumentNumber = number, ReceiptDate = date,
        Currency = Scenario.Usd, Amount = amount, BankAccountCode = "1100",
    };
}
