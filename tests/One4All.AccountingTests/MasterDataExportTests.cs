using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Onboarding;
using One4All.Core.Inventory;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers master-data export: customers, items and the chart are written in their import layouts and read back by the importers into
/// another company without an error; an unknown register is refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class MasterDataExportTests
{
    private readonly AccountingFixture _fixture;

    public MasterDataExportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Exported_registers_load_back_through_their_importers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long source = await Scenario.SeedAsync(db);
        db.AddRange(new Customer(source, "MDX-C1", "Smith, Jones & Co", Scenario.Usd) { CreditLimit = 2500m }, new Item(source, "MDX-I1", "Bolt \"M8\"") { RevenueAccountCode = "4000", Category = "Hardware" });
        await db.SaveChangesAsync();

        IMasterDataExportService export = scope.ServiceProvider.GetRequiredService<IMasterDataExportService>();
        (await export.ExportAsync(source, "Planets")).Error.Code.Should().Be("Import.ExportKind");
        string customers = (await export.ExportAsync(source, "customers")).Value;
        customers.Should().StartWith("code,name,currency,credit_limit").And.Contain("MDX-C1,\"Smith, Jones & Co\",USD,2500");
        string items = (await export.ExportAsync(source, "Items")).Value;
        string accounts = (await export.ExportAsync(source, "Accounts")).Value;
        (await db.ChangeLog.AsNoTracking().CountAsync(e => e.CompanyId == source && e.EntityType == "MasterDataExport" && e.Action == Core.Audit.AuditAction.Export))
            .Should().Be(3, "every export is recorded");

        long target = await Scenario.SeedAsync(db);
        IOpeningBalanceService ob = scope.ServiceProvider.GetRequiredService<IOpeningBalanceService>();
        ImportReport customerReport = await ob.ImportCustomersAsync(target, customers, commit: true);
        customerReport.Errors.Should().BeEmpty();
        customerReport.Created.Should().BeGreaterThan(0);
        (await ob.ImportItemsAsync(target, items, commit: false)).Errors.Should().BeEmpty();
        ImportReport chart = await scope.ServiceProvider.GetRequiredService<IVerticalImportService>().ImportAccountsAsync(target, accounts, commit: false);
        chart.Errors.Should().BeEmpty();
        chart.Skipped.Should().Be(chart.Rows, "the seeded chart already has every account");
    }
}
