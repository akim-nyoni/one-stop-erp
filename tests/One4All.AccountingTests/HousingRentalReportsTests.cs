using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the rental, scheme and partner housing reports (H54–H89): the rent roll shows the let unit with its billed rent as
/// arrears; the void unit carries days void and rent lost; never-inspected units are due; the stock register shows
/// occupancy; and an enumerated settlement appears in the settlement register and the regularisation progress report.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class HousingRentalReportsTests
{
    private readonly AccountingFixture _fixture;

    public HousingRentalReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Rental_and_scheme_reports_reflect_the_registers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Customer tenant = new(companyId, "HRR-T", "Rental Tenant", Scenario.Usd);
        db.Customers.Add(tenant);
        await db.SaveChangesAsync();

        IRentalHousingService rentals = scope.ServiceProvider.GetRequiredService<IRentalHousingService>();
        (await rentals.AddUnitAsync(companyId, "HRR-F1", "Two-room flat", RentalUnitType.Flat, 2, 300m, "4400", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        (await rentals.AddUnitAsync(companyId, "HRR-H1", "Family house", RentalUnitType.House, 4, 365m, "4400", new DateOnly(2026, 4, 1))).IsSuccess.Should().BeTrue();
        (await rentals.LetAsync(new LetUnitRequest { CompanyId = companyId, UnitCode = "HRR-F1", TenantCustomerId = tenant.Id, StartDate = new DateOnly(2026, 3, 1) })).IsSuccess.Should().BeTrue();
        (await rentals.RunRentAsync(companyId, new DateOnly(2026, 3, 1))).IsSuccess.Should().BeTrue();

        IInformalSettlementService settlements = scope.ServiceProvider.GetRequiredService<IInformalSettlementService>();
        (await settlements.RegisterAsync(new SettlementRegistration { CompanyId = companyId, Code = "HRR-S", Name = "Hill Camp", Ward = "Ward 2", CutOffDate = new DateOnly(2025, 12, 31) })).IsSuccess.Should().BeTrue();
        (await settlements.StartEnumerationAsync(companyId, "HRR-S", "E1", new DateOnly(2026, 2, 1))).IsSuccess.Should().BeTrue();
        (await settlements.EnumerateAsync(new HouseholdSurvey
        {
            CompanyId = companyId, SettlementCode = "HRR-S", HeadIdNumber = $"HRR-ID-{companyId}", HeadName = "Head One", HeadDateOfBirth = new DateOnly(1980, 1, 1), HouseholdSize = 3,
            MonthlyIncome = 100m, OccupiedSince = new DateOnly(2019, 1, 1), StructureType = StructureType.Shack, StructureReference = "S-1", Date = new DateOnly(2026, 2, 2),
        })).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            var rendered = await runner.RenderAsync(companyId, code, new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 5, 1) }, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("H71")).Should().Contain("HRR-F1").And.Contain("Rental Tenant").And.Contain("300.00");
        string voids = await Render("H73");
        voids.Should().Contain("HRR-H1").And.Contain("30").And.Contain("360.00").And.NotContain("HRR-F1"); // 30 days at 365 × 12 / 365
        (await Render("H72")).Should().Contain("Void").And.Contain("Let");
        (await Render("H80")).Should().Contain("HRR-F1").And.Contain("Never inspected");
        (await Render("H87")).Should().Contain("HRR-S").And.Contain("Hill Camp").And.Contain("Not approved");
        (await Render("H88")).Should().Contain("HRR-S");
    }
}
