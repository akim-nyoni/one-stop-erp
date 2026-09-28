using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
/// End-to-end working-capital metrics: DSO and DPO computed from a real AR invoice and AP invoice.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class WorkingCapitalAccountingTests
{
    private readonly AccountingFixture _fixture;

    public WorkingCapitalAccountingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Dso_and_dpo_are_computed_from_receivables_and_payables()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long supplierId;
        long itemId;
        long warehouseId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "CUST1", "Acme", Scenario.Usd);
            Supplier supplier = new(companyId, "SUP1", "Widgets", Scenario.Usd);
            Item item = new(companyId, "SODA", "Soda") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            Warehouse wh = new(companyId, "WH1", "Store");
            db.Customers.Add(customer);
            db.Suppliers.Add(supplier);
            db.Items.Add(item);
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            supplierId = supplier.Id;
            itemId = item.Id;
            warehouseId = wh.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "SI-1", DocumentDate = new DateOnly(2026, 1, 15), Currency = Scenario.Usd, NetAmount = 1000m, TaxAmount = 0m, SalesAccountCode = "4000" });

            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "PI-1", DocumentDate = new DateOnly(2026, 1, 15), Currency = Scenario.Usd, NetAmount = 600m, TaxAmount = 0m, ExpenseAccountCode = "6800" });

            // Inventory: receive 100 @ 3.00 (stock 300), issue 20 (COGS 60) → closing stock 240.
            IInventoryService inv = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            await inv.ReceiveAsync(new ReceiveStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 1, 10), Quantity = 100m, UnitCost = 3.00m, CreditAccountCode = "1100" });
            await inv.IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 1, 20), Quantity = 20m, Reference = "ISS-1" });
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IWorkingCapitalService wc = scope.ServiceProvider.GetRequiredService<IWorkingCapitalService>();
            var result = await wc.ComputeAsync(companyId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)); // 31 days

            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            WorkingCapitalMetrics m = result.Value;

            m.ReceivablesBalance.Should().Be(1000m);
            m.CreditSales.Should().Be(1000m);
            m.DaysSalesOutstanding.Should().Be(31.0m); // 1000 / 1000 × 31

            m.PayablesBalance.Should().Be(600m);
            m.CreditPurchases.Should().Be(600m);
            m.DaysPayableOutstanding.Should().Be(31.0m); // 600 / 600 × 31

            m.InventoryBalance.Should().Be(240m);   // 300 received − 60 issued
            m.CostOfSales.Should().Be(60m);         // 20 @ 3.00
            m.DaysInventoryOutstanding.Should().Be(124.0m); // 240 / 60 × 31
            m.CashConversionCycle.Should().Be(124.0m);      // 31 + 124 − 31
        }
    }
}
