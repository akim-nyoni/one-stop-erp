using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers HR records (brief §14 O PR21, PR47, PR55–PR57): a court order collected through payroll down to its balance; establishment
/// against headcount with vacancy savings and over-establishment; expired, expiring and missing documents; lapsed and live warnings;
/// training renewals and an overdue appraisal.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class HrRecordsTests
{
    private readonly AccountingFixture _fixture;

    public HrRecordsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Hr_records_are_kept_and_reported()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement garnishee = new(companyId, "GARN", "Court order", PayElementType.Deduction, CalculationBasis.FixedAmount, 2, "2410") { FixedAmount = 150m };
        db.PayElements.AddRange(basic, garnishee);
        await db.SaveChangesAsync();
        Employee ann = new(companyId, "HR-A", "Ann", 2_000m, new DateOnly(2024, 1, 1)) { Department = "Ops", Grade = "G1" };
        Employee ben = new(companyId, "HR-B", "Ben", 2_000m, new DateOnly(2024, 1, 1)) { Department = "Ops", Grade = "G1" };
        Employee cy = new(companyId, "HR-C", "Cy", 2_500m, new DateOnly(2024, 1, 1)) { Department = "Ops", Grade = "G2" };
        ann.AssignPayElement(basic.Id);
        ann.AssignPayElement(garnishee.Id);
        db.Employees.AddRange(ann, ben, cy);
        await db.SaveChangesAsync();

        IHrRecordsService hr = scope.ServiceProvider.GetRequiredService<IHrRecordsService>();
        (await hr.RegisterGarnisheeAsync(companyId, ann.Id, "CO-1", "Court Sheriff", 1, 150m, 400m, "BASIC", new DateOnly(2026, 1, 1))).Error.Code.Should().Be("Hr.GarnisheeElement");
        (await hr.RegisterGarnisheeAsync(companyId, ann.Id, "co-1", "Court Sheriff", 1, 150m, 400m, "garn", new DateOnly(2026, 1, 1))).IsSuccess.Should().BeTrue();
        IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();
        (await payroll.RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "HR-RUN-1")).IsSuccess.Should().BeTrue();
        (await payroll.RunAsync(companyId, "2026-02", new DateOnly(2026, 2, 25), "2420", "HR-RUN-2")).IsSuccess.Should().BeTrue();

        (await hr.SetEstablishmentAsync(companyId, "Ops", "G1", 2, 12_000m)).IsSuccess.Should().BeTrue();
        (await hr.SetEstablishmentAsync(companyId, "Ops", "G1", 3, 12_000m)).IsSuccess.Should().BeTrue(); // replaces
        (await hr.SetEstablishmentAsync(companyId, "Admin", "G3", 2, 10_000m)).IsSuccess.Should().BeTrue();

        (await hr.FileDocumentAsync(companyId, ann.Id, "id", "ID-A", null)).IsSuccess.Should().BeTrue();
        (await hr.FileDocumentAsync(companyId, ann.Id, "Contract", "C-1", new DateOnly(2026, 3, 10))).IsSuccess.Should().BeTrue();
        (await hr.FileDocumentAsync(companyId, ben.Id, "ID", "ID-B", new DateOnly(2026, 1, 15))).IsSuccess.Should().BeTrue();

        (await hr.RecordDisciplinaryAsync(companyId, ann.Id, new DateOnly(2025, 6, 1), "Late", DisciplinarySanction.VerbalWarning, 2)).IsSuccess.Should().BeTrue();
        (await hr.RecordDisciplinaryAsync(companyId, ben.Id, new DateOnly(2026, 1, 10), "Absent", DisciplinarySanction.WrittenWarning, 2)).IsSuccess.Should().BeTrue();
        (await hr.RecordTrainingAsync(companyId, ann.Id, "First Aid", new DateOnly(2025, 3, 1), new DateOnly(2026, 3, 1), "Red Cross", 50m)).IsSuccess.Should().BeTrue();
        (await hr.RecordAppraisalAsync(companyId, ann.Id, new DateOnly(2025, 2, 1), 6, new DateOnly(2026, 2, 1))).Error.Code.Should().Be("Hr.AppraisalInvalid");
        (await hr.RecordAppraisalAsync(companyId, ann.Id, new DateOnly(2025, 2, 1), 4, new DateOnly(2026, 2, 1))).IsSuccess.Should().BeTrue();

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code)
        {
            Dictionary<string, object?> parameters = new() { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 2, 28), ["AsAtDate"] = new DateOnly(2026, 2, 28), ["Days"] = 30 };
            var rendered = await runner.RenderAsync(companyId, code, parameters, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        (await Render("PR21")).Should().Contain("HR-A Ann,CO-1,Court Sheriff,1,2026-01-01,150.00,400.00,300.00,100");
        (await Render("PR47")).Should().Contain("Ops,G1,3,2,1,0,12000").And.Contain("Ops,G2,0,1,0,1,0").And.Contain("Admin,G3,2,0,2,0,20000");
        (await Render("PR55")).Should().Contain("HR-A Ann,CONTRACT,C-1,2026-03-10,Expiring").And.Contain("HR-B Ben,ID,ID-B,2026-01-15,Expired").And.Contain("HR-B Ben,CONTRACT,-,-,Missing")
            .And.Contain("HR-C Cy,ID,-,-,Missing").And.NotContain("HR-A Ann,ID");
        (await Render("PR56")).Should().Contain("HR-A Ann,2025-06-01,Late,VerbalWarning,2025-12-01,Lapsed").And.Contain("HR-B Ben,2026-01-10,Absent,WrittenWarning,2027-01-10,Live");
        (await Render("PR57")).Should().Contain("Training,HR-A Ann,First Aid (Red Cross),2025-03-01,2026-03-01,Expiring,50").And.Contain("Appraisal,HR-A Ann,Rated 4,2025-02-01,2026-02-01,Overdue,0");
    }
}
