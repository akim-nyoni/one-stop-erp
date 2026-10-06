using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Inventory;
using One4All.Core.Reporting;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers field-level sensitivity in reports: a report runner without cost rights gets the bill-of-materials costing with its cost
/// columns left out; one with stock valuation (ViewCost) sees them.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FieldSensitivityTests
{
    private readonly AccountingFixture _fixture;

    public FieldSensitivityTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Cost_columns_are_left_out_for_users_without_cost_rights()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Item flour = new(companyId, "FS-FLOUR", "Flour"), bread = new(companyId, "FS-BREAD", "Bread");
        Warehouse bakery = new(companyId, "FS-WH", "Bakery");
        db.AddRange(flour, bread, bakery);
        await db.SaveChangesAsync();
        BillOfMaterials bom = new(companyId, "fs-bread", bread.Id, 1m);
        bom.AddLine(flour.Id, 2m);
        db.BillsOfMaterials.Add(bom);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IInventoryService>().ReceiveAsync(new ReceiveStockRequest
        {
            CompanyId = companyId, ItemId = flour.Id, WarehouseId = bakery.Id, Date = new DateOnly(2026, 5, 1), Quantity = 10m, UnitCost = 3m, CreditAccountCode = "1100",
        })).IsSuccess.Should().BeTrue();

        User Make(string name, params (string Code, OperationFlags Flags)[] grants)
        {
            Role role = new($"FS {name} {companyId}");
            foreach ((string code, OperationFlags flags) in grants)
            {
                role.Grant(code, flags);
            }

            db.Roles.Add(role);
            User user = new($"fs-{name}-{companyId}", name);
            db.Users.Add(user);
            db.SaveChanges();
            user.AssignRole(role.Id);
            db.SaveChanges();
            return user;
        }

        User clerk = Make("clerk", ("Reporting.Report", OperationFlags.View));
        User costing = Make("costing", ("Reporting.Report", OperationFlags.View), ("Inventory.Valuation", OperationFlags.View | OperationFlags.ViewCost));
        IAuditedReportRunner reports = scope.ServiceProvider.GetRequiredService<IAuditedReportRunner>();
        async Task<string> Header(long userId)
        {
            AuditedReport run = (await reports.RunAsync(companyId, "INV98", new Dictionary<string, object?>(), ReportFormat.Csv, ReportRunPurpose.View, userId)).Value;
            return Encoding.UTF8.GetString(run.Document.Content).Split('\n')[0];
        }

        string hidden = await Header(clerk.Id), shown = await Header(costing.Id);
        hidden.Should().Contain("Component").And.NotContain("UnitCost");
        shown.Should().Contain("UnitCost");
    }
}
