using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Housing;
using One4All.Core.Procurement;
using One4All.Core.Schools;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RegistersReportRenderTests
{
    private readonly AccountingFixture _fixture;

    public RegistersReportRenderTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Housing_school_and_procurement_registers_render_live()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();

            // Housing: a stand at 25,000 apportioned cost.
            db.Set<HousingStand>().Add(new HousingStand(companyId, landParcelId: 1, schemeId: null, "ERF-100", 250m, 25000m));

            // School: an enrolled student with a bursary.
            Customer guardian = new(companyId, "GRD-1", "Guardian", Scenario.Usd);
            db.Customers.Add(guardian);
            await db.SaveChangesAsync();
            Student student = new(companyId, "ADM-100", "Pupil One", "G1", guardian.Id) { IsBoarder = true };
            student.GrantBursary(BursaryType.Value, 300m);
            db.Students.Add(student);

            // Procurement: an open purchase order for 40 @ 2.50 = 100.
            Supplier supplier = new(companyId, "SUP-1", "Bolt Supplier", Scenario.Usd);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            PurchaseOrder order = new(companyId, supplier.Id, "PO-100", new DateOnly(2026, 3, 1), Scenario.Usd);
            order.AddLine(itemId: 1, "Bolts", quantity: 40m, unitPrice: 2.50m);
            db.Set<PurchaseOrder>().Add(order);
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();

            (await Html(runner, companyId, "HSE-STAND", new())).Should().Contain("ERF-100").And.Contain("25,000.00").And.Contain("Available");
            string sch = await Html(runner, companyId, "SCH-STU", new());
            sch.Should().Contain("ADM-100").And.Contain("Pupil One").And.Contain("300.00");
            (await Html(runner, companyId, "POP-OPEN", new())).Should().Contain("PO-100").And.Contain("Bolt Supplier").And.Contain("100.00");
        }
    }

    private static async Task<string> Html(IReportRunner runner, long companyId, string code, Dictionary<string, object?> parameters)
    {
        var result = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Html);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? $"{code}: {result.Error}" : null);
        return Encoding.UTF8.GetString(result.Value.Content);
    }
}
