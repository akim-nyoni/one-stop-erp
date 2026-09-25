using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class QuotationReportTests
{
    private readonly AccountingFixture _fixture;

    public QuotationReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Quotation_register_lists_quotes_with_value_and_status()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "QR-CUST", "Register Buyer", Scenario.Usd);
            db.Customers.Add(customer);
            await db.SaveChangesAsync();

            IQuotationService quotes = scope.ServiceProvider.GetRequiredService<IQuotationService>();
            // 4 x 25 = 100.
            await quotes.CreateAsync(companyId, customer.Id, "QR-1", new DateOnly(2026, 3, 10), new DateOnly(2026, 4, 10), Scenario.Usd,
                [new QuotationLineRequest(0, "Consulting", 4m, 25m, null, "4000")]);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "SOP-QUOTE", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            html.Should().Contain("QR-1").And.Contain("Register Buyer").And.Contain("Draft").And.Contain("100.00");
        }
    }
}
