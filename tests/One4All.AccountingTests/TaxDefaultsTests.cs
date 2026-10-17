using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Default tax codes (brief §3.5) on suppliers, customers and accounts fill in the tax on invoices entered without one; a
/// category's costing method applies to items created in it; GL26 shows the FX revaluation before and after per trader.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TaxDefaultsTests
{
    private readonly AccountingFixture _fixture;

    public TaxDefaultsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Invoices_without_tax_take_the_trader_or_account_default_code()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long supplierId;
        long plainSupplierId;
        long customerId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
            TaxCode vat = new(companyId, "DVAT", "VAT 15%", TaxType.Standard) { OutputAccountCode = "2200", InputAccountCode = "2210" };
            vat.AddRate(new DateOnly(2020, 1, 1), 15m);
            db.TaxCodes.Add(vat);
            Supplier supplier = new(companyId, "DTX-S", "Taxed Supplier", Scenario.Usd) { DefaultTaxCode = "dvat" };
            Supplier plain = new(companyId, "DTX-P", "Plain Supplier", Scenario.Usd);
            Customer customer = new(companyId, "DTX-C", "Taxed Customer", Scenario.Usd) { DefaultTaxCode = "DVAT" };
            db.Suppliers.AddRange(supplier, plain);
            db.Customers.Add(customer);
            LedgerAccount sales = await db.Accounts.SingleAsync(a => a.CompanyId == companyId && a.AccountCode == "4000");
            sales.DefaultTaxCode = "DVAT";
            await db.SaveChangesAsync();
            (supplierId, plainSupplierId, customerId) = (supplier.Id, plain.Id, customer.Id);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            DateOnly date = new(2026, 6, 5);

            // The supplier's default: 100 net becomes 115 with 15 input tax; an explicit zero-tax invoice with its own code stays as entered.
            var taxed = await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = supplierId, DocumentNumber = "DTX-1", DocumentDate = date, Currency = Scenario.Usd, NetAmount = 100m, ExpenseAccountCode = "6800" });
            taxed.IsSuccess.Should().BeTrue(taxed.IsFailure ? taxed.Error.ToString() : null);
            // No supplier default and no account default: nothing is added.
            var plain = await ap.PostInvoiceAsync(new PurchaseInvoiceRequest { CompanyId = companyId, SupplierId = plainSupplierId, DocumentNumber = "DTX-2", DocumentDate = date, Currency = Scenario.Usd, NetAmount = 100m, ExpenseAccountCode = "6800" });
            plain.IsSuccess.Should().BeTrue(plain.IsFailure ? plain.Error.ToString() : null);
            // The customer's default on a sales invoice: 200 net, 30 output tax.
            var sale = await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "DTX-3", DocumentDate = date, Currency = Scenario.Usd, NetAmount = 200m, SalesAccountCode = "4000" });
            sale.IsSuccess.Should().BeTrue(sale.IsFailure ? sale.Error.ToString() : null);

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.OpenItems.AsNoTracking().SingleAsync(i => i.Id == taxed.Value.OpenItemId)).OriginalTransaction.Should().Be(115m);
            (await db.OpenItems.AsNoTracking().SingleAsync(i => i.Id == plain.Value.OpenItemId)).OriginalTransaction.Should().Be(100m);
            (await db.OpenItems.AsNoTracking().SingleAsync(i => i.Id == sale.Value.OpenItemId)).OriginalTransaction.Should().Be(230m);
            JournalEntry purchase = await db.Journals.AsNoTracking().Include(j => j.Lines).SingleAsync(j => j.CompanyId == companyId && j.DocumentNumber == "DTX-1");
            purchase.Lines.Should().Contain(l => l.AccountCode == "2210" && l.TransactionDebit == 15m && l.TaxCode == "DVAT");
            JournalEntry invoice = await db.Journals.AsNoTracking().Include(j => j.Lines).SingleAsync(j => j.CompanyId == companyId && j.DocumentNumber == "DTX-3");
            invoice.Lines.Should().Contain(l => l.AccountCode == "2200" && l.TransactionCredit == 30m);
        }
    }

    [SkippableFact]
    public async Task Category_costing_applies_to_new_items_and_GL26_shows_revaluation_by_trader()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
            db.ItemCategories.Add(new ItemCategory(companyId, "FIFOCAT", "FIFO goods") { CostingMethod = CostingMethod.Fifo });
            db.Customers.Add(new Customer(companyId, "FX-C", "Euro Debtor", Scenario.Eur));
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IMasterDataService master = scope.ServiceProvider.GetRequiredService<IMasterDataService>();
            long inCategory = (await master.CreateItemAsync(new NewItem(companyId, "FIFO-1", "Takes the category's method", ItemType.Stock, "4000", "5000", Category: "FIFOCAT"))).Value;
            long explicitly = (await master.CreateItemAsync(new NewItem(companyId, "FIFO-2", "Keeps its own", ItemType.Stock, "4000", "5000", Costing: CostingMethod.Standard, StandardCost: 5m, PriceVarianceAccountCode: "5300", Category: "FIFOCAT"))).Value;
            long elsewhere = (await master.CreateItemAsync(new NewItem(companyId, "FIFO-3", "No category", ItemType.Stock, "4000", "5000"))).Value;
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Items.AsNoTracking().SingleAsync(i => i.Id == inCategory)).CostingMethod.Should().Be(CostingMethod.Fifo);
            (await db.Items.AsNoTracking().SingleAsync(i => i.Id == explicitly)).CostingMethod.Should().Be(CostingMethod.Standard);
            (await db.Items.AsNoTracking().SingleAsync(i => i.Id == elsewhere)).CostingMethod.Should().Be(CostingMethod.WeightedAverage);

            // A EUR invoice booked at 1.10; the closing rate on the table differs, so GL26 shows before, after and the difference.
            ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
            long customerId = await db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId && c.Code == "FX-C").Select(c => c.Id).SingleAsync();
            (await ar.PostInvoiceAsync(new SalesInvoiceRequest { CompanyId = companyId, CustomerId = customerId, DocumentNumber = "FX-1", DocumentDate = new DateOnly(2026, 6, 10), Currency = Scenario.Eur, NetAmount = 1000m, SalesAccountCode = "4000" }))
                .IsSuccess.Should().BeTrue();

            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            var rendered = await runner.RenderAsync(companyId, "GL26", new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 6, 30) }, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
            string csv = Encoding.UTF8.GetString(rendered.Value.Content);
            csv.Should().Contain("AR,FX-C,Euro Debtor,EUR,1,").And.Contain("1,000.00").And.Contain("1,200.00");
        }
    }
}
