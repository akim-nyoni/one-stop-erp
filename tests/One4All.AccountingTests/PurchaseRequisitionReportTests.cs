using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class PurchaseRequisitionReportTests
{
    private readonly AccountingFixture _fixture;

    public PurchaseRequisitionReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Requisition_register_lists_requisitions_with_value_and_status()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Supplier supplier = new(companyId, "RQR-SUP", "Register Supplier", Scenario.Usd);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();

            IPurchaseRequisitionService reqs = scope.ServiceProvider.GetRequiredService<IPurchaseRequisitionService>();
            // 5 x 30 = 150.
            await reqs.CreateAsync(companyId, supplier.Id, "RQR-1", new DateOnly(2026, 3, 1), Scenario.Usd,
                [new PurchaseRequisitionLineRequest(0, "Stationery", 5m, 30m)]);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "POP-REQ", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            html.Should().Contain("RQR-1").And.Contain("Register Supplier").And.Contain("Draft").And.Contain("150.00");
        }
    }
}
