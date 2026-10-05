using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Payroll;
using One4All.Core.Security;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers corrections after a payroll is committed: a supplementary run for a committed period, a formally approved reversal (after
/// which the period may be run again), backpay for a retrospective increase, the legacy import for the parallel run, and the
/// PR8–PR10 reports over them.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollAdjustmentTests
{
    private readonly AccountingFixture _fixture;

    public PayrollAdjustmentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Committed_runs_are_corrected_by_visible_runs_and_compared_with_the_legacy_payroll()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        long companyId = await Scenario.SeedAsync(db);

        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        PayElement paye = new(companyId, "PAYE", "PAYE", PayElementType.Deduction, CalculationBasis.Formula, 2, "2400") { Formula = "TAXABLE * 10 / 100" };
        db.PayElements.AddRange(basic, paye);
        User approver = new($"pa-approver-{companyId}", "Payroll Approver");
        db.Users.Add(approver);
        await db.SaveChangesAsync();
        Employee al = new(companyId, "PA-1", "Al One", 1000m, new DateOnly(2025, 1, 1));
        Employee bea = new(companyId, "PA-2", "Bea Two", 2000m, new DateOnly(2025, 1, 1));
        foreach (Employee e in new[] { al, bea })
        {
            e.AssignPayElement(basic.Id);
            e.AssignPayElement(paye.Id);
        }

        db.Employees.AddRange(al, bea);
        await db.SaveChangesAsync();

        IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();
        IPayrollAdjustmentService adjustments = scope.ServiceProvider.GetRequiredService<IPayrollAdjustmentService>();
        (await payroll.RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "PA-01")).Value.TotalNet.Should().Be(2700m);

        // Supplementary: only for a committed period, with a reason, an unused document and known elements.
        SupplementaryRunRequest Supplementary(string period, string document, string reason, params SupplementaryLine[] lines) => new()
        {
            CompanyId = companyId, PeriodLabel = period, PayDate = new DateOnly(2026, 1, 28), NetPayAccountCode = "2420", DocumentNumber = document, Reason = reason, Lines = lines,
        };

        (await adjustments.RunSupplementaryAsync(Supplementary("2026-02", "PA-S0", "Early", new SupplementaryLine(al.Id, "BASIC", 100m)))).Error.Code.Should().Be("Payroll.PeriodNotRun");
        (await adjustments.RunSupplementaryAsync(Supplementary("2026-01", "PA-S0", " ", new SupplementaryLine(al.Id, "BASIC", 100m)))).Error.Code.Should().Be("Payroll.ReasonRequired");
        (await adjustments.RunSupplementaryAsync(Supplementary("2026-01", "PA-S0", "Typo", new SupplementaryLine(al.Id, "BONUS", 100m)))).Error.Code.Should().Be("Payroll.ElementNotFound");
        var supplementary = await adjustments.RunSupplementaryAsync(Supplementary("2026-01", "PA-S1", "Missed acting allowance", new SupplementaryLine(al.Id, "BASIC", 100m),
            new SupplementaryLine(al.Id, "PAYE", 10m)));
        supplementary.IsSuccess.Should().BeTrue(supplementary.IsFailure ? supplementary.Error.ToString() : null);
        supplementary.Value.TotalNet.Should().Be(90m);
        (await adjustments.RunSupplementaryAsync(Supplementary("2026-01", "PA-S1", "Again", new SupplementaryLine(al.Id, "BASIC", 1m)))).Error.Code.Should().Be("Payroll.DuplicateRun");

        // Reversal: approved by a user, once, never of a reversal; the period can then be run again — once.
        (await payroll.RunAsync(companyId, "2026-02", new DateOnly(2026, 2, 25), "2420", "PA-02")).IsSuccess.Should().BeTrue();
        (await adjustments.ReverseRunAsync(companyId, "PA-02", new DateOnly(2026, 2, 26), "Wrong rates", 0)).Error.Code.Should().Be("Payroll.ApproverNotFound");
        (await adjustments.ReverseRunAsync(companyId, "PA-02", new DateOnly(2026, 2, 26), " ", approver.Id)).Error.Code.Should().Be("Payroll.ReasonRequired");
        var reversal = await adjustments.ReverseRunAsync(companyId, "PA-02", new DateOnly(2026, 2, 26), "Wrong rates", approver.Id);
        reversal.IsSuccess.Should().BeTrue(reversal.IsFailure ? reversal.Error.ToString() : null);
        reversal.Value.TotalNet.Should().Be(-2700m);
        (await adjustments.ReverseRunAsync(companyId, "PA-02", new DateOnly(2026, 2, 26), "Again", approver.Id)).Error.Code.Should().Be("Payroll.AlreadyReversed");
        (await adjustments.ReverseRunAsync(companyId, "REV-PA-02", new DateOnly(2026, 2, 26), "Undo", approver.Id)).Error.Code.Should().Be("Payroll.CannotReverseReversal");
        (await payroll.RunAsync(companyId, "2026-02", new DateOnly(2026, 2, 27), "2420", "PA-02B")).IsSuccess.Should().BeTrue();
        (await payroll.RunAsync(companyId, "2026-02", new DateOnly(2026, 2, 27), "2420", "PA-02C")).Error.Code.Should().Be("Payroll.PeriodAlreadyRun");

        // Backpay: Bea's 2,200 from January — both periods short by 200 basic and 20 PAYE.
        IReadOnlyList<BackpayPreviewLine> owed = (await adjustments.CalculateBackpayAsync(companyId, bea.Id, 2200m, new DateOnly(2026, 1, 1))).Value;
        owed.Should().BeEquivalentTo(new[]
        {
            new BackpayPreviewLine("2026-01", "BASIC", 2000m, 2200m), new BackpayPreviewLine("2026-01", "PAYE", 200m, 220m),
            new BackpayPreviewLine("2026-02", "BASIC", 2000m, 2200m), new BackpayPreviewLine("2026-02", "PAYE", 200m, 220m),
        });
        BackpayRequest Backpay(string document) => new()
        {
            CompanyId = companyId, EmployeeId = bea.Id, NewBasicSalary = 2200m, EffectiveFrom = new DateOnly(2026, 1, 1), PeriodLabel = "2026-03", PayDate = new DateOnly(2026, 3, 25),
            NetPayAccountCode = "2420", DocumentNumber = document, Reason = "Award 2026",
        };
        var backpay = await adjustments.PayBackpayAsync(Backpay("PA-BP1"));
        backpay.IsSuccess.Should().BeTrue(backpay.IsFailure ? backpay.Error.ToString() : null);
        backpay.Value.TotalNet.Should().Be(360m);
        (await db.Employees.AsNoTracking().SingleAsync(e => e.Id == bea.Id)).BasicSalary.Should().Be(2200m);
        (await adjustments.CalculateBackpayAsync(companyId, bea.Id, 2200m, new DateOnly(2026, 1, 1))).Value.Should().BeEmpty();
        (await adjustments.PayBackpayAsync(Backpay("PA-BP2"))).Error.Code.Should().Be("Payroll.NoBackpay");

        // Legacy import for the parallel run (header skipped, a bad line refused).
        (await adjustments.ImportLegacyPayrollAsync(companyId, "2026-01", "Employee,Element,Amount\nPA-1,BASIC\n")).Error.Code.Should().Be("Payroll.LegacyLine");
        (await adjustments.ImportLegacyPayrollAsync(companyId, "2026-01", "Employee,Element,Amount\nPA-1,BASIC,1100\nPA-1,NET,990\npa-2,basic,2000\nPA-2,PAYE,190\nPA-2,NET,1810\n"))
            .Value.Should().Be(5);

        // The ledger: net pay carries January (2,700 + 90), February once (2,700) and the backpay (360).
        TrialBalanceReport tb = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>().TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Should().Contain(r => r.AccountCode == "2420" && r.Credit == 5850m);

        IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
        async Task<string> Render(string code, DateOnly from, DateOnly to)
        {
            var rendered = await runner.RenderAsync(companyId, code, new Dictionary<string, object?> { ["FromDate"] = from, ["ToDate"] = to }, ReportFormat.Csv);
            rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? $"{code}: {rendered.Error.Message}" : null);
            return Encoding.UTF8.GetString(rendered.Value.Content);
        }

        string parallel = await Render("PR8", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        parallel.Should().Contain("2026-01,PA-1 Al One,BASIC,\"1,100.00\",\"1,100.00\",Match,0")
            .And.Contain("2026-01,PA-1 Al One,PAYE,110.00,-,Not in legacy,110")
            .And.Contain("2026-01,PA-2 Bea Two,PAYE,200.00,190.00,Differs,10")
            .And.Contain("2026-01,PA-2 Bea Two,NET,\"1,800.00\",\"1,810.00\",Differs,-10");

        string register = await Render("PR9", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));
        register.Should().Contain("PA-S1,2026-01,Supplementary,2026-01-28,Missed acting allowance,1,")
            .And.Contain($"PA-02,2026-02,Regular,2026-02-25,-,2,").And.Contain($"REV-PA-02 on 2026-02-26, approved by pa-approver-{companyId}")
            .And.Contain("REV-PA-02,2026-02,Reversal,2026-02-26,Wrong rates,2,")
            .And.Contain("PA-BP1,2026-03,Backpay,2026-03-25,Award 2026,1,")
            .And.NotContain("PA-02B");

        string backpayReport = await Render("PR10", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
        backpayReport.Should().Contain("PA-2 Bea Two,2026-01,BASIC,\"2,000.00\",\"2,200.00\",PA-BP1,2026-03-25,Award 2026,200")
            .And.Contain("PA-2 Bea Two,2026-02,PAYE,200.00,220.00,PA-BP1,2026-03-25,Award 2026,20");
    }
}
