using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the control reconciliation for a past date with later activity: an allocation and a stock issue
/// after the date must not leak into it, so March still reconciles after April's receipt and issue - and
/// April reconciles too.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ControlReconciliationAsAtTests
{
    private readonly AccountingFixture _fixture;

    public ControlReconciliationAsAtTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Past_date_reconciliation_ignores_later_allocations_and_movements()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "CRA-C", "Buyer", Scenario.Usd);
            db.Customers.Add(customer);
            Supplier supplier = new(companyId, "CRA-S", "Supplier", Scenario.Usd);
            db.Suppliers.Add(supplier);
            Item item = new(companyId, "CRA-ITEM", "Thing") { CostOfSalesAccountCode = "5000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "CRAWH", "Main");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();

            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            IAllocationService alloc = scope.ServiceProvider.GetRequiredService<IAllocationService>();
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();

            // March: an invoice, a supplier invoice, and stock received.
            long invoice = (await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "CRA-I1", DocumentDate = new DateOnly(2026, 3, 10), Currency = Scenario.Usd, NetAmount = 1_000m, SalesAccountCode = "4000" })).Value.OpenItemId;
            long purchase = (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "CRA-P1", DocumentDate = new DateOnly(2026, 3, 12), Currency = Scenario.Usd, NetAmount = 500m, ExpenseAccountCode = "6800" })).Value.OpenItemId;
            (await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = item.Id, WarehouseId = wh.Id, Date = new DateOnly(2026, 3, 1), Quantity = 100m, UnitCost = 2m, CreditAccountCode = "1100" })).IsSuccess.Should().BeTrue();

            // April: the customer pays 400 (allocated), we pay the supplier 500 (allocated), and 30 units are issued.
            long receipt = (await ar.PostReceiptAsync(new ReceiptRequest { CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "CRA-R1", ReceiptDate = new DateOnly(2026, 4, 5), Currency = Scenario.Usd, Amount = 400m, BankAccountCode = "1100" })).Value.OpenItemId;
            (await alloc.AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoice, receipt, 400m, new DateOnly(2026, 4, 5))).IsSuccess.Should().BeTrue();
            long payment = (await ap.PostPaymentAsync(new PaymentRequest { CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "CRA-PAY", PaymentDate = new DateOnly(2026, 4, 8), Currency = Scenario.Usd, Amount = 500m, BankAccountCode = "1100" })).Value.OpenItemId;
            (await alloc.AllocateAsync(companyId, TradingLedger.AccountsPayable, payment, purchase, 500m, new DateOnly(2026, 4, 8))).IsSuccess.Should().BeTrue();
            (await inv.IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = item.Id, WarehouseId = wh.Id, Date = new DateOnly(2026, 4, 2), Quantity = 30m })).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();

            // As at March, after April's activity has happened: the sub-ledgers as they stood in March.
            ControlReconciliationReport march = await gl.ControlReconciliationAsync(companyId, new DateOnly(2026, 3, 31));
            Row(march, "Accounts Receivable").Should().BeEquivalentTo(new { GlBalance = 1_000m, SubLedgerBalance = 1_000m, Difference = 0m });
            Row(march, "Accounts Payable").Should().BeEquivalentTo(new { GlBalance = 500m, SubLedgerBalance = 500m, Difference = 0m });
            Row(march, "Stock").Should().BeEquivalentTo(new { GlBalance = 200m, SubLedgerBalance = 200m, Difference = 0m });

            ControlReconciliationReport april = await gl.ControlReconciliationAsync(companyId, new DateOnly(2026, 4, 30));
            Row(april, "Accounts Receivable").Should().BeEquivalentTo(new { GlBalance = 600m, SubLedgerBalance = 600m, Difference = 0m });
            Row(april, "Accounts Payable").Should().BeEquivalentTo(new { GlBalance = 0m, SubLedgerBalance = 0m, Difference = 0m });
            Row(april, "Stock").Should().BeEquivalentTo(new { GlBalance = 140m, SubLedgerBalance = 140m, Difference = 0m });
        }
    }

    private static ControlReconciliationRow Row(ControlReconciliationReport report, string control) => report.Rows.Single(r => r.Control == control);
}
