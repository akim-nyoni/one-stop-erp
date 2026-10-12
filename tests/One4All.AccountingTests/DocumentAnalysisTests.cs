using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers trading documents posting their revenue and expense lines with cost centre and department, and multi-line purchase invoices.</summary>
[Collection(AccountingCollection.Name)]
public sealed class DocumentAnalysisTests
{
    private readonly AccountingFixture _fixture;

    public DocumentAnalysisTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Invoices_carry_their_analysis_to_the_ledger()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await Scenario.DimensionsAsync(db, companyId, "FIN", "OPS");
        Customer customer = new(companyId, "DA-CUS", "Analysed customer", Scenario.Usd);
        Supplier supplier = new(companyId, "DA-SUP", "Analysed supplier", Scenario.Usd);
        db.AddRange(customer, supplier);
        await db.SaveChangesAsync();

        TradingPostingResult sale = (await scope.ServiceProvider.GetRequiredService<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "DA-INV", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd, NetAmount = 100m,
            SalesAccountCode = "4000", Department = "fin", CostCentre = "FIN",
        })).Value;

        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        PurchaseInvoiceRequest Purchase(params RevenueLine[] lines) => new()
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = $"DA-PI-{lines.Length}", DocumentDate = new DateOnly(2026, 3, 2), Currency = Scenario.Usd, NetAmount = 90m,
            ExpenseLines = lines, Department = "OPS",
        };
        (await ap.PostInvoiceAsync(Purchase(new RevenueLine("6800", 50m)))).Error.Code.Should().Be("Ap.ExpenseLinesMismatch");
        TradingPostingResult purchase = (await ap.PostInvoiceAsync(Purchase(new RevenueLine("6800", 60m), new RevenueLine("6700", 30m, null, "FIN")))).Value;

        List<JournalLine> lines = await (from l in db.Set<JournalLine>().AsNoTracking()
                                         join j in db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                                         where j.CompanyId == companyId && (j.JournalNumber == sale.JournalNumber || j.JournalNumber == purchase.JournalNumber)
                                         select l).ToListAsync();
        lines.Single(l => l.AccountCode == "4000").Should().Match<JournalLine>(l => l.Department == "FIN" && l.CostCentre == "FIN");
        lines.Single(l => l.AccountCode == "6800").Department.Should().Be("OPS");
        lines.Single(l => l.AccountCode == "6700").Department.Should().Be("FIN", "a line's own department wins over the document's");
        lines.Where(l => l.AccountCode == "1200" || l.AccountCode == "2100").Should().OnlyContain(l => string.IsNullOrEmpty(l.Department));
    }
}
