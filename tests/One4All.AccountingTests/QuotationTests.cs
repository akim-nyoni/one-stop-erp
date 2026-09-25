using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class QuotationTests
{
    private readonly AccountingFixture _fixture;

    public QuotationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Accepted_quotation_converts_into_a_sales_order()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long customerId;
        long itemId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "Q-CUST", "Quote Buyer", Scenario.Usd);
            db.Customers.Add(customer);
            Item item = new(companyId, "Q-ITEM", "Widget") { CostOfSalesAccountCode = "5000", RevenueAccountCode = "4000" };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            itemId = item.Id;
        }

        long quoteId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IQuotationService quotes = scope.ServiceProvider.GetRequiredService<IQuotationService>();

            // Validity cannot precede the quote date; a quote needs lines.
            (await quotes.CreateAsync(companyId, customerId, "Q-BAD", new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 1), Scenario.Usd,
                [new QuotationLineRequest(itemId, "Widget", 5m, 10m, null, "4000")])).IsFailure.Should().BeTrue();
            (await quotes.CreateAsync(companyId, customerId, "Q-EMPTY", new DateOnly(2026, 3, 10), new DateOnly(2026, 4, 10), Scenario.Usd, [])).IsFailure.Should().BeTrue();

            var create = await quotes.CreateAsync(companyId, customerId, "Q-1", new DateOnly(2026, 3, 10), new DateOnly(2026, 4, 10), Scenario.Usd,
                [new QuotationLineRequest(itemId, "Widget", 5m, 10m, null, "4000")]);
            create.IsSuccess.Should().BeTrue(create.IsFailure ? create.Error.ToString() : null);
            quoteId = create.Value;

            // Cannot convert before acceptance.
            (await quotes.ConvertToOrderAsync(companyId, quoteId, "SO-Q1", new DateOnly(2026, 3, 12))).IsFailure.Should().BeTrue();

            (await quotes.SendAsync(companyId, quoteId)).IsSuccess.Should().BeTrue();
            (await quotes.AcceptAsync(companyId, quoteId)).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IQuotationService quotes = scope.ServiceProvider.GetRequiredService<IQuotationService>();
            var convert = await quotes.ConvertToOrderAsync(companyId, quoteId, "SO-Q1", new DateOnly(2026, 3, 12));
            convert.IsSuccess.Should().BeTrue(convert.IsFailure ? convert.Error.ToString() : null);

            // A converted quote cannot convert again.
            (await quotes.ConvertToOrderAsync(companyId, quoteId, "SO-Q2", new DateOnly(2026, 3, 13))).IsFailure.Should().BeTrue();

            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            Quotation quote = await db.Set<Quotation>().FirstAsync(q => q.Id == quoteId);
            quote.Status.Should().Be(QuotationStatus.Converted);
            quote.SalesOrderId.Should().Be(convert.Value);

            SalesOrder order = await db.Set<SalesOrder>().Include(o => o.Lines).FirstAsync(o => o.Id == convert.Value);
            order.OrderNumber.Should().Be("SO-Q1");
            order.Lines.Should().ContainSingle(l => l.ItemId == itemId && l.OrderedQuantity == 5m && l.UnitPrice == 10m);
        }
    }
}
