using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers settlement discount suggestions and their tax: a customer with 2% for payment within 10 days is offered 23.00 on an invoice of
/// 1,000 + 150 tax paid on day 5 and nothing on day 15; settling with the output tax account moves the 3.00 tax share of the discount off
/// output tax; a supplier's 575 invoice (75 input tax) paid with a 23 discount moves 3.00 off input tax.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SettlementDiscountTaxTests
{
    private readonly AccountingFixture _fixture;

    public SettlementDiscountTaxTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task The_discount_is_suggested_within_the_window_and_its_tax_share_comes_off_tax()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Customer customer = new(companyId, "SDT-CUS", "Early payer", Scenario.Usd) { SettlementDiscountPercent = 2m, SettlementDiscountDays = 10 };
        Supplier supplier = new(companyId, "SDT-SUP", "Discounting supplier", Scenario.Usd);
        db.AddRange(customer, supplier);
        await db.SaveChangesAsync();

        ISalesLedgerService ar = scope.ServiceProvider.GetRequiredService<ISalesLedgerService>();
        long invoice = (await ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId, CustomerId = customer.Id, DocumentNumber = "SDT-1", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd,
            NetAmount = 1_000m, TaxAmount = 150m, SalesAccountCode = "4000", TaxAccountCode = "2200",
        })).Value.OpenItemId;
        (await ar.SuggestSettlementDiscountAsync(companyId, invoice, new DateOnly(2026, 3, 5))).Value.Should().Be(23m);
        (await ar.SuggestSettlementDiscountAsync(companyId, invoice, new DateOnly(2026, 3, 15))).Value.Should().Be(0m);
        (await ar.SettleWithDiscountAsync(companyId, invoice, new DateOnly(2026, 3, 5), 1_127m, 23m, "1100", "4700", "SDT-R1", "2200")).IsSuccess.Should().BeTrue();

        IPurchaseLedgerService ap = scope.ServiceProvider.GetRequiredService<IPurchaseLedgerService>();
        long bill = (await ap.PostInvoiceAsync(new PurchaseInvoiceRequest
        {
            CompanyId = companyId, SupplierId = supplier.Id, DocumentNumber = "SDT-B1", DocumentDate = new DateOnly(2026, 3, 1), Currency = Scenario.Usd,
            NetAmount = 500m, TaxAmount = 75m, ExpenseAccountCode = "6800", TaxAccountCode = "2210",
        })).Value.OpenItemId;
        (await ap.PayWithDiscountAsync(companyId, bill, new DateOnly(2026, 3, 5), 552m, 23m, "1100", "4950", "SDT-P1", "2210")).IsSuccess.Should().BeTrue();

        Dictionary<string, decimal> net = await (from l in db.Set<JournalLine>().AsNoTracking()
                                                 join j in db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                                                 where j.CompanyId == companyId
                                                 group l by l.AccountCode into g
                                                 select new { g.Key, Net = g.Sum(l => l.BaseDebit - l.BaseCredit) }).ToDictionaryAsync(x => x.Key, x => x.Net);
        net["2200"].Should().Be(-147m, "150 output tax less the 3.00 share of the discount");
        net["4700"].Should().Be(20m, "the discount allowed net of its tax");
        net["2210"].Should().Be(72m, "75 input tax less the 3.00 share of the discount received");
        net["4950"].Should().Be(-20m, "the discount received net of its tax");
    }
}
