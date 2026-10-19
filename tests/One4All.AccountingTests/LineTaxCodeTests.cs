using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Mixed-rate invoices: each line's own tax code, its tax worked out on its lines and posted to its accounts.</summary>
[Collection(AccountingCollection.Name)]
public sealed class LineTaxCodeTests
{
    private readonly AccountingFixture _fixture;

    public LineTaxCodeTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Each_line_is_taxed_by_its_own_code()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IMasterDataService master = scope.ServiceProvider.GetRequiredService<IMasterDataService>();
        DateOnly from = new(2026, 1, 1);
        (await master.CreateTaxCodeAsync(new NewTaxCode(companyId, "LT-STD", "Standard", TaxType.Standard, 15m, from, "2200", "2210"))).IsSuccess.Should().BeTrue();
        (await master.CreateTaxCodeAsync(new NewTaxCode(companyId, "LT-ZERO", "Zero rated", TaxType.Zero, 0m, from, "2200", "2210"))).IsSuccess.Should().BeTrue();
        (await master.CreateTaxCodeAsync(new NewTaxCode(companyId, "LT-RC", "Imported services", TaxType.ReverseCharge, 15m, from, "2200", "2210"))).IsSuccess.Should().BeTrue();
        Customer buyer = new(companyId, "LT-C", "Buyer", Scenario.Usd);
        Supplier seller = new(companyId, "LT-S", "Seller", Scenario.Usd);
        db.AddRange(buyer, seller);
        await db.SaveChangesAsync();

        // Sales: 100 standard (15) and 50 zero-rated (0) - 15 tax, 165 owed.
        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        TradingPostingResult sale = (await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = buyer.Id, DocumentNumber = "LT-INV1", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 150m, SalesAccountCode = "4000",
            RevenueLines = [new RevenueLine("4000", 100m, TaxCode: "lt-std"), new RevenueLine("4000", 50m, TaxCode: "LT-ZERO")],
        })).Value;
        (await db.OpenItems.AsNoTracking().SingleAsync(o => o.Id == sale.OpenItemId)).OriginalTransaction.Should().Be(165m);
        (await Net(db, companyId, sale.JournalNumber))["2200"].Should().Be(-15m);
        (await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = buyer.Id, DocumentNumber = "LT-INV2", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 150m, SalesAccountCode = "4000",
            RevenueLines = [new RevenueLine("4000", 100m, TaxCode: "LT-STD"), new RevenueLine("4000", 50m)],
        })).Error.Code.Should().Be("Tax.LineTaxCodes");

        // Purchases: 200 standard (30 input) and 100 reverse charge (15 self-assessed) - the supplier is owed 330.
        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        TradingPostingResult bill = (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = seller.Id, DocumentNumber = "LT-B1", DocumentDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd, NetAmount = 300m, ExpenseAccountCode = "6800",
            ExpenseLines = [new RevenueLine("6800", 200m, TaxCode: "LT-STD"), new RevenueLine("6800", 100m, TaxCode: "LT-RC")],
        })).Value;
        (await db.OpenItems.AsNoTracking().SingleAsync(o => o.Id == bill.OpenItemId)).OriginalTransaction.Should().Be(330m);
        Dictionary<string, decimal> lines = await Net(db, companyId, bill.JournalNumber);
        (lines["2210"], lines["2200"], lines["6800"]).Should().Be((45m, -15m, 300m));
    }

    private static async Task<Dictionary<string, decimal>> Net(One4AllDbContext db, long companyId, string journal)
        => (await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.JournalNumber == journal).SelectMany(j => j.Lines).ToListAsync())
            .GroupBy(l => l.AccountCode).ToDictionary(g => g.Key, g => g.Sum(l => l.BaseDebit - l.BaseCredit));
}
