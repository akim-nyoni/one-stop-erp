using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class ItemMasterReportTests
{
    private readonly AccountingFixture _fixture;

    public ItemMasterReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Item_master_lists_items_with_uom_and_accounts()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Item widget = new(companyId, "IM-1", "Widget") { UnitOfMeasure = "EA", ReorderLevel = 25m, RevenueAccountCode = "4000", CostOfSalesAccountCode = "5000" };
            db.Items.Add(widget);
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "INV-ITEMS", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            html.Should().Contain("IM-1").And.Contain("Widget").And.Contain("EA").And.Contain("Yes");
        }
    }
}
