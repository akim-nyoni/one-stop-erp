using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RecurringInvoiceReportTests
{
    private readonly AccountingFixture _fixture;

    public RecurringInvoiceReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Recurring_invoice_register_lists_templates_with_amount_and_next_run()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Customer customer = new(companyId, "RR-CUST", "Retainer Client", Scenario.Usd);
            db.Customers.Add(customer);
            await db.SaveChangesAsync();

            IRecurringSalesInvoiceService svc = scope.ServiceProvider.GetRequiredService<IRecurringSalesInvoiceService>();
            await svc.CreateAsync(companyId, customer.Id, "RETAINER", Scenario.Usd, 250m, 0m, "4000", null, 1, new DateOnly(2026, 5, 1), "Monthly retainer");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "AR-RECUR", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            html.Should().Contain("RETAINER").And.Contain("Retainer Client").And.Contain("250.00").And.Contain("Yes");
        }
    }
}
