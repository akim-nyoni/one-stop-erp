using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Budgeting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers budget planning (brief §14 B3–B8, B10, B32): ceilings against drafts, the hierarchy roll-up, drivers and a what-if, the basis of
/// each line, an establishment-driven staff budget, a driver-built revenue line and a budget holder's statement.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetPlanningTests
{
    private readonly AccountingFixture _fixture;

    public BudgetPlanningTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Planning_data_drives_the_budget_preparation_reports()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await Scenario.DimensionsAsync(db, companyId, "WRK", "PRK");
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId && y.Code == "2026").Select(y => y.Id).SingleAsync();

        Budget works1 = new(companyId, yearId, "WORKS", BudgetType.DraftSubmission, "WRK", 1), works2 = new(companyId, yearId, "WORKS", BudgetType.DraftSubmission, "WRK", 2);
        Budget parks = new(companyId, yearId, "PARKS", BudgetType.DraftSubmission, "PRK", 1);
        Budget fees = new(companyId, yearId, "FEES", BudgetType.OriginalApproved, null, 1);
        Budget staff = new(companyId, yearId, "STAFF", BudgetType.OriginalApproved, "WRK", 1);
        for (int period = 1; period <= 12; period++)
        {
            works1.AddLine("6000", period, 100m);
            works2.AddLine("6000", period, 90m).Basis = BudgetBasis.ZeroBased;
            parks.AddLine("6000", period, 50m);
            BudgetLine fee = fees.AddLine("4000", period, 1_000m);
            fee.Basis = BudgetBasis.DriverBased;
            fee.DriverCode = "ENROL";
        }

        staff.AddLine("6100", 1, 20_000m);
        foreach (Budget approved in new[] { fees, staff })
        {
            approved.Approve();
            approved.Activate();
        }

        db.AddRange(works1, works2, parks, fees, staff,
            new BudgetCeiling(companyId, yearId, "WRK", 1_000m), new BudgetCeiling(companyId, yearId, "PRK", 800m), new BudgetCeiling(companyId, yearId, "Roads", 500m),
            new DepartmentUnit(companyId, "WRK", "Infrastructure", "Technical") { BudgetHolder = "W. Boss" }, new DepartmentUnit(companyId, "PRK", "Amenities", "Technical"),
            new BudgetDriver(companyId, yearId, "enrol", "Enrolment", "students", 400m),
            new EstablishmentPost(companyId, "WRK", "G1", 2, 12_000m), new Employee(companyId, "BP-1", "Works Hand", 1_000m, new DateOnly(2024, 1, 1)) { Department = "WRK" });
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 2, 28), TransactionCurrency = Scenario.Usd, DocumentType = "Journal", DocumentNumber = "BP-PAY", Narrative = "Wages",
            Lines =
            [
                new JournalLineRequest { AccountCode = "6100", Side = JournalSide.Debit, TransactionAmount = 2_000m, Department = "WRK" },
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 2_000m },
            ],
        })).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code, Dictionary<string, object?>? extra = null)
        {
            Dictionary<string, object?> parameters = new() { ["AsAtDate"] = new DateOnly(2026, 3, 31) };
            foreach (KeyValuePair<string, object?> pair in extra ?? [])
            {
                parameters[pair.Key] = pair.Value;
            }

            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("B3")).Should().Contain("WRK,\"1,000.00\",\"1,080.00\",Over ceiling,80").And.Contain("PRK,800.00,600.00,Within,-200").And.Contain("Roads,500.00,not submitted,No draft,0");
        (await Render("B4")).Should().Contain("Directorate,Technical,2,1680");
        (await Render("B4", new() { ["Level"] = "Division" })).Should().Contain("Division,Infrastructure,1,1080").And.Contain("Division,Amenities,1,600");
        (await Render("B5")).Should().Contain("ENROL,Enrolment,students,400,1,12000");
        (await Render("B6", new() { ["DriverCode"] = "enrol", ["NewValue"] = 420m })).Should().Contain("FEES v1,4000,All,\"12,000.00\",\"12,600.00\",600");
        (await Render("B7")).Should().Contain("WORKS v2,WRK,6000,ZeroBased,—,1080").And.Contain("FEES v1,All,4000,DriverBased,ENROL,12000");
        (await Render("B8", new() { ["StaffAccount"] = "6100", ["VacancyPercent"] = 10m })).Should().Contain("WRK,2,1,\"24,000.00\",10%,\"21,600.00\",\"20,000.00\",-1600");
        (await Render("B10")).Should().Contain("FEES v1,4000,").And.Contain(",ENROL,400,30.00,12000");
        (await Render("B32")).Should().Contain("W. Boss,WRK,6100,\"20,000.00\",\"2,000.00\",0.00,10.0%,18000");
    }
}
