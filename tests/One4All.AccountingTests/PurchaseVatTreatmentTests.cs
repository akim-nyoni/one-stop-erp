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

/// <summary>Fourteenth audit #8: a reverse-charge purchase is self-assessed (the supplier owed the net only) and a partly recoverable code splits input tax from cost.</summary>
[Collection(AccountingCollection.Name)]
public sealed class PurchaseVatTreatmentTests
{
    private readonly AccountingFixture _fixture;

    public PurchaseVatTreatmentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Reverse_charge_and_partial_recovery_post_as_the_code_says()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IMasterDataService master = scope.ServiceProvider.GetRequiredService<IMasterDataService>();
        DateOnly from = new(2026, 1, 1);
        (await master.CreateTaxCodeAsync(new NewTaxCode(companyId, "PVT-RC", "Imported services", TaxType.ReverseCharge, 15m, from, "2200", "2210"))).IsSuccess.Should().BeTrue();
        (await master.CreateTaxCodeAsync(new NewTaxCode(companyId, "PVT-PR", "Standard, 40% recoverable", TaxType.Standard, 15m, from, "2200", "2210"))).IsSuccess.Should().BeTrue();
        TaxCode partly = await db.TaxCodes.SingleAsync(t => t.CompanyId == companyId && t.Code == "PVT-PR");
        partly.RecoverablePercent = 40m;
        Supplier abroad = new(companyId, "PVT-S", "Overseas Consulting", Scenario.Usd);
        db.Suppliers.Add(abroad);
        await db.SaveChangesAsync();
        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();

        // Reverse charge: 1,000 of services, no VAT on the invoice; 150 self-assessed - output 150, input 150; the supplier is owed 1,000.
        TradingPostingResult rc = (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = abroad.Id, DocumentNumber = "PVT-1", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 1_000m, ExpenseAccountCode = "6800",
            TaxCode = "PVT-RC",
        })).Value;
        (await db.OpenItems.AsNoTracking().SingleAsync(o => o.Id == rc.OpenItemId)).OriginalTransaction.Should().Be(1_000m, "the supplier is never owed the self-assessed tax");
        Dictionary<string, decimal> lines = await Net(db, companyId, rc.JournalNumber);
        (lines["2200"], lines["2210"], lines["6800"]).Should().Be((-150m, 150m, 1_000m));

        // 40% recoverable: 100 net + 15 tax; 6 claimed as input, 9 is cost on the expense account; the supplier is owed 115.
        TradingPostingResult partial = (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = abroad.Id, DocumentNumber = "PVT-2", DocumentDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd, NetAmount = 100m, TaxAmount = 15m,
            ExpenseAccountCode = "6800", TaxCode = "PVT-PR", TaxAccountCode = "2210",
        })).Value;
        (await db.OpenItems.AsNoTracking().SingleAsync(o => o.Id == partial.OpenItemId)).OriginalTransaction.Should().Be(115m);
        lines = await Net(db, companyId, partial.JournalNumber);
        (lines["2210"], lines["6800"]).Should().Be((6m, 109m));
    }

    private static async Task<Dictionary<string, decimal>> Net(One4AllDbContext db, long companyId, string journal)
        => (await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.JournalNumber == journal).SelectMany(j => j.Lines).ToListAsync())
            .GroupBy(l => l.AccountCode).ToDictionary(g => g.Key, g => g.Sum(l => l.BaseDebit - l.BaseCredit));
}
