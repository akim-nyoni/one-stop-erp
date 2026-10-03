using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers reconciling a supplier statement against real AP postings: invoices pair by number, an unapplied
/// payment pairs by amount, a fully settled invoice drops out of both sides, and the balance identity holds.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SupplierStatementReconciliationTests
{
    private readonly AccountingFixture _fixture;

    public SupplierStatementReconciliationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Supplier_statement_reconciles_to_the_purchase_ledger()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier supplier = new(companyId, "SSR-S", "Statement Supplier", Scenario.Usd);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            supplierId = supplier.Id;

            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            IAllocationService alloc = scope.ServiceProvider.GetRequiredService<IAllocationService>();

            // A settled invoice (paid and allocated) — it should appear on neither side.
            long settledInvoice = (await ap.PostInvoiceAsync(Invoice(companyId, supplierId, "S-100", new DateOnly(2026, 4, 1), 400m))).Value.OpenItemId;
            long settledPayment = (await ap.PostPaymentAsync(Payment(companyId, supplierId, "PAY-1", new DateOnly(2026, 4, 20), 400m))).Value.OpenItemId;
            (await alloc.AllocateAsync(companyId, TradingLedger.AccountsPayable, settledPayment, settledInvoice, 400m, new DateOnly(2026, 4, 20))).IsSuccess.Should().BeTrue();

            // Outstanding: two invoices, and a payment on account not yet allocated.
            (await ap.PostInvoiceAsync(Invoice(companyId, supplierId, "S-101", new DateOnly(2026, 5, 2), 600m))).IsSuccess.Should().BeTrue();
            (await ap.PostInvoiceAsync(Invoice(companyId, supplierId, "S-102", new DateOnly(2026, 5, 9), 250m))).IsSuccess.Should().BeTrue();
            (await ap.PostPaymentAsync(Payment(companyId, supplierId, "PAY-2", new DateOnly(2026, 5, 20), 300m))).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISupplierStatementService service = scope.ServiceProvider.GetRequiredService<ISupplierStatementService>();

            // Their statement: S-101 agrees, S-102 they show at 275, an invoice S-103 we have not posted, and our payment in their words.
            SupplierStatementLine[] statement =
            [
                new("S-101", new DateOnly(2026, 5, 2), 600m),
                new("S 102", new DateOnly(2026, 5, 9), 275m),
                new("S-103", new DateOnly(2026, 5, 28), 90m),
                new("Receipt - thank you", new DateOnly(2026, 5, 22), -300m),
            ];

            var result = await service.ReconcileAsync(companyId, supplierId, statement);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            SupplierReconciliation recon = result.Value;

            recon.LedgerBalance.Should().Be(550m);    // 600 + 250 − 300
            recon.StatementBalance.Should().Be(665m); // 600 + 275 + 90 − 300
            recon.Difference.Should().Be(115m);       // 25 price difference + 90 unposted invoice
            recon.Lines.Sum(l => l.Difference).Should().Be(recon.Difference);

            recon.Lines.Should().NotContain(l => l.DocumentNumber == "S-100" || l.DocumentNumber == "PAY-1");
            recon.Lines.Single(l => l.DocumentNumber == "S-101").Status.Should().Be(SupplierReconStatus.Agreed);
            recon.Lines.Single(l => l.DocumentNumber == "S 102").Status.Should().Be(SupplierReconStatus.AmountDiffers);
            recon.Lines.Single(l => l.DocumentNumber == "S-103").Status.Should().Be(SupplierReconStatus.MissingFromLedger);
            SupplierReconLine payment = recon.Lines.Single(l => l.StatementAmount == -300m);
            payment.Status.Should().Be(SupplierReconStatus.Agreed);
            payment.MatchedByAmount.Should().BeTrue();

            (await service.ReconcileAsync(companyId, 999_999, statement)).Error.Code.Should().Be("Ap.SupplierNotFound");
        }
    }

    private static PurchaseInvoiceRequest Invoice(long companyId, long supplierId, string number, DateOnly date, decimal net) => new()
    {
        CompanyId = companyId, SupplierId = supplierId, DocumentNumber = number, DocumentDate = date,
        Currency = Scenario.Usd, NetAmount = net, ExpenseAccountCode = "5000",
    };

    private static PaymentRequest Payment(long companyId, long supplierId, string number, DateOnly date, decimal amount) => new()
    {
        CompanyId = companyId, SupplierId = supplierId, DocumentNumber = number, PaymentDate = date,
        Currency = Scenario.Usd, Amount = amount, BankAccountCode = "1100",
    };
}
