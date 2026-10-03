using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the duplicate supplier-invoice control on both entry routes: direct purchase-ledger entry and the
/// three-way match. The same number (however punctuated) for the same supplier is refused; another supplier
/// may use it; nothing is posted for a refused invoice.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DuplicateSupplierInvoiceTests
{
    private readonly AccountingFixture _fixture;

    public DuplicateSupplierInvoiceTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_same_supplier_invoice_cannot_be_entered_twice()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        long otherSupplierId;
        long itemId;
        long warehouseId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier supplier = new(companyId, "DUP-S", "Supplier", Scenario.Usd);
            Supplier other = new(companyId, "DUP-O", "Other Supplier", Scenario.Usd);
            db.Suppliers.AddRange(supplier, other);
            Item item = new(companyId, "DUP-ITEM", "Thing") { CostOfSalesAccountCode = "5000" };
            db.Items.Add(item);
            Warehouse wh = new(companyId, "DUPWH", "Main");
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            supplierId = supplier.Id;
            otherSupplierId = other.Id;
            itemId = item.Id;
            warehouseId = wh.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();

            (await ap.PostInvoiceAsync(Invoice(companyId, supplierId, "INV-0042", 100m))).IsSuccess.Should().BeTrue();

            // The same invoice keyed differently is still the same invoice.
            var again = await ap.PostInvoiceAsync(Invoice(companyId, supplierId, "inv 0042", 100m));
            again.Error.Code.Should().Be("Ap.DuplicateInvoice");
            again.Error.Message.Should().Contain("INV-0042");

            // Another supplier's invoice may carry the same number; a different number is fine.
            (await ap.PostInvoiceAsync(Invoice(companyId, otherSupplierId, "INV-0042", 70m))).IsSuccess.Should().BeTrue();
            (await ap.PostInvoiceAsync(Invoice(companyId, supplierId, "INV-0043", 50m))).IsSuccess.Should().BeTrue();

            (await ap.PostInvoiceAsync(Invoice(companyId, supplierId, "  ", 10m))).Error.Code.Should().Be("Ap.InvoiceNumberRequired");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            // Through the three-way match: receive goods, then try to match an invoice number already entered.
            IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            long poId = (await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = supplierId, OrderNumber = "DUP-PO", OrderDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd,
                Lines = [new PurchaseOrderLineRequest(itemId, "Thing", 10m, 5m)],
            })).Value.PurchaseOrderId;
            long lineId = await db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == poId).Select(l => l.Id).SingleAsync();
            (await pop.ReceiveGoodsAsync(new ReceiveGoodsRequest { CompanyId = companyId, PurchaseOrderId = poId, WarehouseId = warehouseId, GrnNumber = "DUP-GRN", Date = new DateOnly(2026, 3, 2), Lines = [new ReceiveLine(lineId, 10m)] })).IsSuccess.Should().BeTrue();

            var duplicate = await pop.MatchInvoiceAsync(new MatchInvoiceRequest
            {
                CompanyId = companyId, PurchaseOrderId = poId, InvoiceNumber = "INV/0043", Date = new DateOnly(2026, 3, 3),
                Lines = [new InvoiceMatchLine(lineId, 10m, 5m)],
            });
            duplicate.Error.Code.Should().Be("Ap.DuplicateInvoice");

            var genuine = await pop.MatchInvoiceAsync(new MatchInvoiceRequest
            {
                CompanyId = companyId, PurchaseOrderId = poId, InvoiceNumber = "INV-0044", Date = new DateOnly(2026, 3, 3),
                Lines = [new InvoiceMatchLine(lineId, 10m, 5m)],
            });
            genuine.IsSuccess.Should().BeTrue(genuine.IsFailure ? genuine.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            // Only the accepted invoices exist: 0042, 0043 and 0044 for the supplier.
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            List<string> numbers = await db.OpenItems
                .Where(i => i.CompanyId == companyId && i.TraderId == supplierId && i.Ledger == TradingLedger.AccountsPayable && i.ItemType == OpenItemType.Invoice)
                .OrderBy(i => i.DocumentNumber).Select(i => i.DocumentNumber).ToListAsync();
            numbers.Should().Equal("INV-0042", "INV-0043", "INV-0044");
        }
    }

    private static PurchaseInvoiceRequest Invoice(long companyId, long supplierId, string number, decimal net) => new()
    {
        CompanyId = companyId, SupplierId = supplierId, DocumentNumber = number, DocumentDate = new DateOnly(2026, 3, 1),
        Currency = Scenario.Usd, NetAmount = net, ExpenseAccountCode = "5000",
    };
}
