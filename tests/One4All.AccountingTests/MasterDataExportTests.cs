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

        // The registers added later export in their import layouts too: dimensions, assets, loans, stock.
        db.CostCentres.Add(new Core.Accounting.CostCentre(source, "MDX", "Head office"));
        db.FixedAssets.Add(new Core.FixedAssets.FixedAsset(source, "MDX-FA1", "Toyota Hilux", "Vehicles", 30_000m, 5_000m, new DateOnly(2026, 1, 15), Core.FixedAssets.DepreciationMethod.StraightLine,
            60, 0m, "1600", "1610", "6000"));
        await db.SaveChangesAsync();
        (await export.ExportAsync(source, "CostCentres")).Value.Should().StartWith("code,name").And.Contain("MDX,Head office");
        (await export.ExportAsync(source, "fixedassets")).Value.Should().Contain("MDX-FA1,Toyota Hilux,Vehicles,2026-01-15,30000");
        (await export.ExportAsync(source, "StaffLoans")).Value.Should().StartWith("employee_number,reference,outstanding,instalment,loan_account");
        (await export.ExportAsync(source, "Stock")).Value.Should().StartWith("item,warehouse,quantity,unit_cost");
        export.Kinds.Should().Contain(["Stock", "FixedAssets", "StaffLoans", "CostCentres", "Departments", "Students", "Stands"]);
    }
}
