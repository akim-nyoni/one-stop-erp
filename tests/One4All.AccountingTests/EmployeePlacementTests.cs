using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Accounting;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the employee record's placement and personal details (brief §4.15.1): branch, project and fund drive the payroll journal; whom they
/// report to, next of kin, marital status, nationality, social security number and union are kept; unknown placements are refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class EmployeePlacementTests
{
    private readonly AccountingFixture _fixture;

    public EmployeePlacementTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Placement_drives_the_payroll_journal_and_details_are_kept()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Branches.Add(new Branch(companyId, "BYO", "Bulawayo"));
        db.Projects.AddRange(new Project(companyId, "ROADS", "Roads programme"), new Project(companyId, "OLD", "Finished") { IsActive = false });
        db.Funds.Add(new Fund(companyId, "DONOR", "Donor fund", isRestricted: true));
        PayElement basic = new(companyId, "BASIC", "Basic", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        db.PayElements.Add(basic);
        await db.SaveChangesAsync();
        IPayrollSetupService setup = scope.ServiceProvider.GetRequiredService<IPayrollSetupService>();

        long manager = (await setup.CreateEmployeeAsync(companyId, new NewEmployee("PL-MGR", "Mona Manager", 4000m, new DateOnly(2025, 1, 1)))).Value;
        long engineer = (await setup.CreateEmployeeAsync(companyId, new NewEmployee("PL-ENG", "Eli Engineer", 3000m, new DateOnly(2025, 1, 1))
        {
            Branch = "byo", Project = "roads", Fund = "DONOR", ReportsTo = "PL-MGR", MaritalStatus = MaritalStatus.Married, Nationality = "Zimbabwean",
            SocialSecurityNumber = "NSSA-123", UnionMembership = "ZCTU", NextOfKinName = "Esi Engineer", NextOfKinPhone = "+263 77 000 0000", NextOfKinRelationship = "Spouse",
        })).Value;

        Employee saved = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == engineer);
        (saved.Project, saved.Fund, saved.ReportsToEmployeeId, saved.MaritalStatus, saved.SocialSecurityNumber, saved.NextOfKinRelationship)
            .Should().Be(("ROADS", "DONOR", manager, MaritalStatus.Married, "NSSA-123", "Spouse"));
        saved.BranchId.Should().Be(await db.Branches.Where(b => b.CompanyId == companyId && b.Code == "BYO").Select(b => b.Id).SingleAsync());

        (await setup.CreateEmployeeAsync(companyId, new NewEmployee("PL-X", "X", 1m, new DateOnly(2025, 1, 1)) { Branch = "NOWHERE" })).Error.Code.Should().Be("Payroll.BranchUnknown");
        (await setup.CreateEmployeeAsync(companyId, new NewEmployee("PL-X", "X", 1m, new DateOnly(2025, 1, 1)) { Project = "OLD" })).Error.Code.Should().Be("Payroll.ProjectUnknown");
        (await setup.CreateEmployeeAsync(companyId, new NewEmployee("PL-X", "X", 1m, new DateOnly(2025, 1, 1)) { ReportsTo = "PL-NOBODY" })).IsFailure.Should().BeTrue();
        (await setup.UpdateEmployeeAsync(companyId, "PL-ENG", new EmployeeUpdate { ReportsTo = "PL-ENG" })).Error.Code.Should().Be("Payroll.ReportsToSelf");

        Employee charged = await db.Employees.SingleAsync(e => e.Id == engineer);
        charged.AssignPayElement(basic.Id);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-02", new DateOnly(2026, 2, 25), "2420", "PL-2026-02")).IsSuccess.Should().BeTrue();
        JournalLine cost = await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.SourceModule == "PAYROLL" || j.CompanyId == companyId && j.DocumentNumber == "PL-2026-02")
            .SelectMany(j => j.Lines).SingleAsync(l => l.AccountCode == "6100" && l.BaseDebit == 3000m);
        (cost.Project, cost.Fund, cost.Branch).Should().Be(("ROADS", "DONOR", "BYO"));
    }
}
