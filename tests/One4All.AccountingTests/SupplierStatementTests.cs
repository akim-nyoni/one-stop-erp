using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class SupplierStatementTests
{
    private readonly AccountingFixture _fixture;

    public SupplierStatementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Supplier_statement_lists_outstanding_payables_and_the_balance()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier supplier = new(companyId, "SS-SUP", "Statement Supplier", Scenario.Usd) { PaymentTermsDays = 30 };
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            supplierId = supplier.Id;
        }

        long invoice1ItemId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            invoice1ItemId = (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "SS-P1", DocumentDate = new DateOnly(2026, 2, 1), Currency = Scenario.Usd, NetAmount = 100m, ExpenseAccountCode = "5000" })).Value.OpenItemId;
            await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "SS-P2", DocumentDate = new DateOnly(2026, 2, 15), Currency = Scenario.Usd, NetAmount = 200m, ExpenseAccountCode = "5000" });
        }

        long paymentItemId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            // Pay 60 off the first invoice and allocate it.
            paymentItemId = (await ap.PostPaymentAsync(new PaymentRequest { CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "SS-PAY1", PaymentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, Amount = 60m, BankAccountCode = "1100" })).Value.OpenItemId;

            IAllocationService alloc = scope.ServiceProvider.GetRequiredService<IAllocationService>();
            await alloc.AllocateAsync(companyId, TradingLedger.AccountsPayable, paymentItemId, invoice1ItemId, 60m, new DateOnly(2026, 3, 1));
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ITradingReports reports = scope.ServiceProvider.GetRequiredService<ITradingReports>();

            (await reports.SupplierStatementAsync(companyId, 999999)).Should().BeNull(); // unknown supplier

            TraderStatement? statement = await reports.SupplierStatementAsync(companyId, supplierId);
            statement.Should().NotBeNull();
            statement!.TraderCode.Should().Be("SS-SUP");
            // P1 (100 − 60 = 40 outstanding) and P2 (200) remain; the fully-allocated payment drops off.
            statement.Lines.Should().Contain(l => l.DocumentNumber == "SS-P1" && l.Outstanding == 40m);
            statement.Lines.Should().Contain(l => l.DocumentNumber == "SS-P2" && l.Outstanding == 200m);
            statement.Balance.Should().Be(240m);
        }
    }
}
