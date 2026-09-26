using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class TraderRegisterReportTests
{
    private readonly AccountingFixture _fixture;

    public TraderRegisterReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Customer_and_supplier_registers_list_master_data()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            db.Customers.Add(new Customer(companyId, "REG-C1", "Acme Buyer", Scenario.Usd) { CreditLimit = 5000m, PaymentTermsDays = 45, OnHold = true });
            db.Suppliers.Add(new Supplier(companyId, "REG-S1", "Globex Vendor", Scenario.Usd) { PaymentTermsDays = 60 });
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();

            string customers = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "AR-CUST", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            customers.Should().Contain("REG-C1").And.Contain("Acme Buyer").And.Contain("5,000.00").And.Contain("Yes");

            string suppliers = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "AP-SUP", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            suppliers.Should().Contain("REG-S1").And.Contain("Globex Vendor").And.Contain("USD");
        }
    }
}
