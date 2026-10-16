using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Schools;
using One4All.Core.Schools;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using One4All.Reporting;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the SC-, JC- and SO-numbered reports (brief §14 J, K, H): a term billed to two students, one on a 25% bursary;
/// the billing summary, arrears, bursaries, enrolment and balances read it back; every SC / JC / SO report renders.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class OperationsReportsTests
{
    private readonly AccountingFixture _fixture;

    public OperationsReportsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task School_job_and_order_reports_reflect_their_registers()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        Customer guardianA = new(companyId, "OP-GA", "Guardian A", Scenario.Usd);
        Customer guardianB = new(companyId, "OP-GB", "Guardian B", Scenario.Usd);
        db.Customers.AddRange(guardianA, guardianB);
        FeeStructure structure = new(companyId, "OP-G1T1", "Grade 1 Term 1", "G1", "Term 1", "4200");
        structure.AddItem("Tuition", 900m, true);
        structure.AddItem("Levy", 100m, true);
        db.FeeStructures.Add(structure);
        await db.SaveChangesAsync();
        Student full = new(companyId, "OP-001", "Student One", "G1", guardianA.Id) { Gender = One4All.Core.Payroll.Gender.Female };
        Student bursary = new(companyId, "OP-002", "Student Two", "G1", guardianB.Id) { Gender = One4All.Core.Payroll.Gender.Male };
        bursary.GrantBursary(BursaryType.Percentage, 25m);
        db.Students.AddRange(full, bursary);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<ISchoolBillingService>().BillTermAsync(companyId, structure.Id, "OPF-", new DateOnly(2026, 1, 15))).Value.TotalNetBilled.Should().Be(1750m);

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 4, 30), ["AsAtDate"] = new DateOnly(2026, 4, 30) };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("SC175")).Should().Contain("Term 1,G1,2,0,1750");
        (await Render("SC176")).Should().Contain("G1,Student One (OP-001),Guardian A,1,").And.Contain("Student Two (OP-002)");
        (await Render("SC178")).Should().Contain("Student Two (OP-002),G1,Percentage,25%,\"1,000.00\",250");
        (await Render("SC180")).Should().Contain("G1,2,0,2,1,0,1,1");
        (await Render("SC182")).Should().Contain("OP-001,Student One,G1,Guardian A,Enrolled,1000").And.Contain("OP-002,Student Two,G1,Guardian B,Enrolled,750");

        foreach (string code in OperationsReports.All().Select(r => r.Code))
        {
            await Render(code);
        }
    }
}
