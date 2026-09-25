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
public sealed class RecurringPurchaseReportTests
{
    private readonly AccountingFixture _fixture;

    public RecurringPurchaseReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Recurring_purchase_register_lists_templates()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier supplier = new(companyId, "RP-SUP", "Cloud Provider", Scenario.Usd);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();

            IRecurringPurchaseInvoiceService svc = scope.ServiceProvider.GetRequiredService<IRecurringPurchaseInvoiceService>();
            await svc.CreateAsync(companyId, supplier.Id, "HOSTING", Scenario.Usd, 90m, 0m, "5000", null, 1, new DateOnly(2026, 5, 1), "Monthly hosting");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "AP-RECUR", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            html.Should().Contain("HOSTING").And.Contain("Cloud Provider").And.Contain("90.00").And.Contain("Yes");
        }
    }
}
