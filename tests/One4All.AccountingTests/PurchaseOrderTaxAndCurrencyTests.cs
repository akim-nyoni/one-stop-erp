using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// The purchase order invoice match (audit finding 16): input VAT from the order line's tax code with the supplier owed the gross; a foreign-currency
/// order received at the receipt date's rate and invoiced at the invoice date's, the difference an exchange gain or loss apart from any price variance;
/// and a price difference on goods still in stock put on the stock's value rather than all expensed.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PurchaseOrderTaxAndCurrencyTests
{
    // No other test uses the pula, so its rates here are the only ones.
    private static readonly CurrencyCode Bwp = CurrencyCode.Parse("BWP");

    private readonly AccountingFixture _fixture;

    public PurchaseOrderTaxAndCurrencyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_taxed_order_line_posts_input_vat_and_the_supplier_is_owed_the_gross()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        long itemId;
        long taxedItemId;
        long warehouseId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            TaxCode vat = new(companyId, "POTX", "VAT 15%", TaxType.Standard) { OutputAccountCode = "2200", InputAccountCode = "2210" };
            vat.AddRate(new DateOnly(2020, 1, 1), 15m);
            db.TaxCodes.Add(vat);
            Supplier supplier = new(companyId, "POTX-S", "Taxed Supplier", Scenario.Usd);
            Item item = new(companyId, "POTX-A", "Untaxed by default") { CostOfSalesAccountCode = "5000" };
            Item taxedItem = new(companyId, "POTX-B", "Taxed by default") { CostOfSalesAccountCode = "5000", TaxCode = "POTX" };
            Warehouse wh = new(companyId, "POTX-W", "Main");
            db.Suppliers.Add(supplier);
            db.Items.AddRange(item, taxedItem);
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            (supplierId, itemId, taxedItemId, warehouseId) = (supplier.Id, item.Id, taxedItem.Id, wh.Id);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

            // The first line is given the code; the second takes its item's.
            var po = await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = supplierId, OrderNumber = "POTX-1", OrderDate = new DateOnly(2026, 4, 1), Currency = Scenario.Usd,
                Lines = [new PurchaseOrderLineRequest(itemId, "A", 10m, 20m, TaxCode: "potx"), new PurchaseOrderLineRequest(taxedItemId, "B", 5m, 10m)],
            });
            po.IsSuccess.Should().BeTrue(po.IsFailure ? po.Error.ToString() : null);
            List<PurchaseOrderLine> lines = await db.Set<PurchaseOrderLine>().AsNoTracking().Where(l => l.PurchaseOrderId == po.Value.PurchaseOrderId).OrderBy(l => l.Id).ToListAsync();
            lines.Select(l => l.TaxCode).Should().Equal("POTX", "POTX");

            (await pop.ReceiveGoodsAsync(new ReceiveGoodsRequest
            {
                CompanyId = companyId, PurchaseOrderId = po.Value.PurchaseOrderId, WarehouseId = warehouseId, GrnNumber = "POTX-GRN", Date = new DateOnly(2026, 4, 2),
                Lines = [new ReceiveLine(lines[0].Id, 10m), new ReceiveLine(lines[1].Id, 5m)],
            })).IsSuccess.Should().BeTrue();

            // 250 net, 37.50 input VAT: the supplier is owed 287.50 and GRNI clears at the 250 accrued.
            var match = await pop.MatchInvoiceAsync(new MatchInvoiceRequest
            {
                CompanyId = companyId, PurchaseOrderId = po.Value.PurchaseOrderId, InvoiceNumber = "POTX-INV", Date = new DateOnly(2026, 4, 5),
                Lines = [new InvoiceMatchLine(lines[0].Id, 10m, 20m), new InvoiceMatchLine(lines[1].Id, 5m, 10m)],
            });
            match.IsSuccess.Should().BeTrue(match.IsFailure ? match.Error.ToString() : null);
            match.Value.InvoiceValue.Should().Be(287.50m);

            OpenItem owed = await db.OpenItems.AsNoTracking().SingleAsync(i => i.Id == match.Value.OpenItemId);
            owed.OriginalTransaction.Should().Be(287.50m);
            owed.OriginalBase.Should().Be(287.50m);

            JournalEntry journal = await db.Journals.AsNoTracking().Include(j => j.Lines).SingleAsync(j => j.CompanyId == companyId && j.JournalNumber == match.Value.JournalNumber);
            journal.Lines.Where(l => l.AccountCode == "2210").Sum(l => l.BaseDebit).Should().Be(37.50m);
            journal.Lines.Single(l => l.AccountCode == "2210").TaxCode.Should().Be("POTX");
            journal.Lines.Where(l => l.AccountCode == "2150").Sum(l => l.BaseDebit).Should().Be(250m);
            journal.Lines.Where(l => l.AccountCode == "2100").Sum(l => l.BaseCredit).Should().Be(287.50m);
        }
    }

    [SkippableFact]
    public async Task A_foreign_order_is_received_at_the_receipt_rate_and_invoiced_at_the_invoice_rate_with_the_difference_an_exchange_gain()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        long itemId;
        long warehouseId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            if (!await db.Currencies.AnyAsync(c => c.Code == Bwp))
            {
                db.Currencies.Add(new Currency(Bwp, "Botswana pula", "P"));
            }

            if (!await db.ExchangeRates.AnyAsync(r => r.FromCurrency == Bwp && r.ToCurrency == Scenario.Usd && r.RateType == ExchangeRateType.Daily))
            {
                db.ExchangeRates.Add(new ExchangeRate(Bwp, Scenario.Usd, ExchangeRateType.Daily, new DateOnly(2026, 4, 1), 0.075m));
                db.ExchangeRates.Add(new ExchangeRate(Bwp, Scenario.Usd, ExchangeRateType.Daily, new DateOnly(2026, 4, 20), 0.070m));
            }

            Supplier supplier = new(companyId, "POFX-S", "Pula Supplier", Bwp);
            Item item = new(companyId, "POFX-A", "Imported part") { CostOfSalesAccountCode = "5000" };
            Warehouse wh = new(companyId, "POFX-W", "Main");
            db.Suppliers.Add(supplier);
            db.Items.Add(item);
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            (supplierId, itemId, warehouseId) = (supplier.Id, item.Id, wh.Id);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
            IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

            var po = await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = supplierId, OrderNumber = "POFX-1", OrderDate = new DateOnly(2026, 4, 1), Currency = Bwp,
                Lines = [new PurchaseOrderLineRequest(itemId, "Part", 100m, 10m)],
            });
            po.IsSuccess.Should().BeTrue(po.IsFailure ? po.Error.ToString() : null);
            long lineId = (await db.Set<PurchaseOrderLine>().AsNoTracking().SingleAsync(l => l.PurchaseOrderId == po.Value.PurchaseOrderId)).Id;

            // 1,000 pula received on 2 April at 0.075: 75 dollars into stock and GRNI.
            var grn = await pop.ReceiveGoodsAsync(new ReceiveGoodsRequest
            {
                CompanyId = companyId, PurchaseOrderId = po.Value.PurchaseOrderId, WarehouseId = warehouseId, GrnNumber = "POFX-GRN", Date = new DateOnly(2026, 4, 2),
                Lines = [new ReceiveLine(lineId, 100m)],
            });
            grn.IsSuccess.Should().BeTrue(grn.IsFailure ? grn.Error.ToString() : null);
            grn.Value.AccruedValue.Should().Be(75m);
            (await inventory.StockValuationAsync(companyId)).Should().Contain(l => l.ItemCode == "POFX-A" && l.Quantity == 100m && l.Value == 75m);

            // Invoiced on 21 April at 0.070: owed 1,000 pula (70 dollars); GRNI clears at the 75 accrued and the 5 is an exchange gain, no price variance.
            var match = await pop.MatchInvoiceAsync(new MatchInvoiceRequest
            {
                CompanyId = companyId, PurchaseOrderId = po.Value.PurchaseOrderId, InvoiceNumber = "POFX-INV", Date = new DateOnly(2026, 4, 21),
                Lines = [new InvoiceMatchLine(lineId, 100m, 10m)],
            });
            match.IsSuccess.Should().BeTrue(match.IsFailure ? match.Error.ToString() : null);

            OpenItem owed = await db.OpenItems.AsNoTracking().SingleAsync(i => i.Id == match.Value.OpenItemId);
            owed.Currency.Should().Be(Bwp);
            owed.OriginalTransaction.Should().Be(1_000m);
            owed.OriginalBase.Should().Be(70m);

            JournalEntry journal = await db.Journals.AsNoTracking().Include(j => j.Lines).SingleAsync(j => j.CompanyId == companyId && j.JournalNumber == match.Value.JournalNumber);
            journal.Lines.Where(l => l.AccountCode == "2150").Sum(l => l.BaseDebit).Should().Be(75m);
            journal.Lines.Where(l => l.AccountCode == "2100").Sum(l => l.BaseCredit).Should().Be(70m);
            journal.Lines.Where(l => l.AccountCode == "7100").Sum(l => l.BaseCredit).Should().Be(5m);
            journal.Lines.Sum(l => l.BaseDebit).Should().Be(journal.Lines.Sum(l => l.BaseCredit));
            (await inventory.StockValuationAsync(companyId)).Should().Contain(l => l.ItemCode == "POFX-A" && l.Value == 75m, "the exchange difference is not a cost of the stock");
        }
    }

    [SkippableFact]
    public async Task A_price_difference_goes_onto_the_stock_still_held_and_only_the_rest_to_variance()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        long itemId;
        long warehouseId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier supplier = new(companyId, "POPV-S", "Supplier", Scenario.Usd) { InvoicePriceTolerancePercent = 10m };
            Item item = new(companyId, "POPV-A", "Part") { CostOfSalesAccountCode = "5000" };
            Warehouse wh = new(companyId, "POPV-W", "Main");
            db.Suppliers.Add(supplier);
            db.Items.Add(item);
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();
            (supplierId, itemId, warehouseId) = (supplier.Id, item.Id, wh.Id);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IProcurementService pop = scope.ServiceProvider.GetRequiredService<IProcurementService>();
            IInventoryService inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();

            var po = await pop.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
            {
                CompanyId = companyId, SupplierId = supplierId, OrderNumber = "POPV-1", OrderDate = new DateOnly(2026, 4, 1), Currency = Scenario.Usd,
                Lines = [new PurchaseOrderLineRequest(itemId, "Part", 10m, 20m)],
            });
            long lineId = (await db.Set<PurchaseOrderLine>().AsNoTracking().SingleAsync(l => l.PurchaseOrderId == po.Value.PurchaseOrderId)).Id;
            (await pop.ReceiveGoodsAsync(new ReceiveGoodsRequest
            {
                CompanyId = companyId, PurchaseOrderId = po.Value.PurchaseOrderId, WarehouseId = warehouseId, GrnNumber = "POPV-GRN", Date = new DateOnly(2026, 4, 2),
                Lines = [new ReceiveLine(lineId, 10m)],
            })).IsSuccess.Should().BeTrue();
            (await inventory.IssueAsync(new IssueStockRequest { CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = new DateOnly(2026, 4, 3), Quantity = 4m }))
                .IsSuccess.Should().BeTrue();

            // Invoiced at 21 instead of 20: 10 more, of which the 6 still in stock goes onto its value and the 4 already issued to variance.
            var match = await pop.MatchInvoiceAsync(new MatchInvoiceRequest
            {
                CompanyId = companyId, PurchaseOrderId = po.Value.PurchaseOrderId, InvoiceNumber = "POPV-INV", Date = new DateOnly(2026, 4, 5), PriceVarianceAccountCode = "5000",
                Lines = [new InvoiceMatchLine(lineId, 10m, 21m)],
            });
            match.IsSuccess.Should().BeTrue(match.IsFailure ? match.Error.ToString() : null);

            JournalEntry journal = await db.Journals.AsNoTracking().Include(j => j.Lines).SingleAsync(j => j.CompanyId == companyId && j.JournalNumber == match.Value.JournalNumber);
            journal.Lines.Where(l => l.AccountCode == "1400").Sum(l => l.BaseDebit).Should().Be(6m);
            journal.Lines.Where(l => l.AccountCode == "5000").Sum(l => l.BaseDebit).Should().Be(4m);
            journal.Lines.Where(l => l.AccountCode == "2100").Sum(l => l.BaseCredit).Should().Be(210m);
            (await inventory.StockValuationAsync(companyId)).Should().Contain(l => l.ItemCode == "POPV-A" && l.Quantity == 6m && l.Value == 126m);
        }
    }
}
