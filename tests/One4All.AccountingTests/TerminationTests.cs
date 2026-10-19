using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Payroll;
using One4All.Core.Accounting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers termination and final pay: a retrenchment pays the last month pro rata, notice, leave, gratuity and severance, taxes ordinary
/// pay on the PAYE table and terminal benefits at the terminal rate, recovers the loan, posts one balanced journal, terminates the
/// employee (the next run leaves them out) and opens a clearance checklist and certificate of service; a resignation carries leave only,
/// and loans beyond the pay stay owed.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class TerminationTests
{
    private readonly AccountingFixture _fixture;

    public TerminationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Leavers_are_paid_off_and_cleared()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await Scenario.DimensionsAsync(db, companyId, "WKS");
        db.Accounts.Add(new LedgerAccount(companyId, "6150", "Terminal Benefits", AccountType.Expense));
        await db.SaveChangesAsync();
        IPayrollSetupService setup = scope.ServiceProvider.GetRequiredService<IPayrollSetupService>();
        (await setup.CreateTaxTableAsync(companyId, "PAYE", new DateOnly(2026, 1, 1), null,
            [new TaxBandInput(0m, 100m, 0m), new TaxBandInput(100m, 300m, 20m), new TaxBandInput(300m, null, 30m)])).IsSuccess.Should().BeTrue();
        (await setup.SeedStandardElementsAsync(companyId, "6100", "2400", "PAYE")).IsSuccess.Should().BeTrue();
        long retrenched = (await setup.CreateEmployeeAsync(companyId, new NewEmployee("TM-001", "Tafadzwa Ncube", 1_000m, new DateOnly(2020, 1, 15)) { Grade = "B1", Department = "WKS" })).Value;
        long resigning = (await setup.CreateEmployeeAsync(companyId, new NewEmployee("TM-002", "Nyasha Dube", 300m, new DateOnly(2025, 3, 1)))).Value;
        _ = (await setup.CreateEmployeeAsync(companyId, new NewEmployee("TM-003", "Stays On", 400m, new DateOnly(2024, 1, 1)))).Value;
        foreach (string number in new[] { "TM-001", "TM-002", "TM-003" })
        {
            (await setup.AssignElementAsync(companyId, number, "BASIC")).IsSuccess.Should().BeTrue();
        }

        EmployeeLeaveBalance leave = new(companyId, retrenched, "Annual");
        leave.Accrue(10m);
        leave.Take(2m);
        EmployeeLeaveBalance sick = new(companyId, retrenched, "Sick");
        sick.Accrue(30m);
        db.AddRange(leave, sick, new EmployeeLoan(companyId, retrenched, "LN-1", 200m, 50m, new DateOnly(2025, 1, 1), "1350"), new EmployeeLoan(companyId, resigning, "LN-2", 900m, 100m, new DateOnly(2025, 6, 1), "1350"));
        await db.SaveChangesAsync();

        ITerminationService terminations = scope.ServiceProvider.GetRequiredService<ITerminationService>();
        TerminationRequest Request(string number, TerminationType type) => new(companyId, number, type, new DateOnly(2026, 4, 15), "Restructuring")
        {
            Policy = new TerminalPolicy(1m, 22m, 2m, 4m, 10m), SalaryExpenseAccount = "6100", TerminalBenefitsAccount = "6150", PayeLiabilityAccount = "2400", NetPayAccount = "2420", ApprovedByUserId = 5,
        };

        // Retrenchment: 6 years; 15 of 30 April days; tax 0 + 40 + 30% of 1,563.64; terminal tax 10%.
        var previewResult = await terminations.PreviewAsync(Request("TM-001", TerminationType.Retrenchment));
        previewResult.IsSuccess.Should().BeTrue(previewResult.IsFailure ? previewResult.Error.ToString() : null);
        FinalPayStatement preview = previewResult.Value;
        (preview.YearsOfService, preview.ProRataSalary, preview.Notice, preview.LeaveDays, preview.Leave, preview.Gratuity, preview.Severance)
            .Should().Be((6, 500m, 1_000m, 8m, 363.64m, 2_769.23m, 5_538.46m));
        (preview.OrdinaryTax, preview.TerminalTax, preview.Recoveries, preview.UnrecoveredLoans, preview.Net).Should().Be((509.09m, 830.77m, 200m, 0m, 8_631.47m));
        (await db.Employees.AsNoTracking().SingleAsync(e => e.Id == retrenched)).Status.Should().Be(EmployeeStatus.Active, "a preview changes nothing");

        // On a terminal tax table instead: 1,000 tax-free, the first 5,000 of the rest at 0 and 20% above - 20% of 2,307.69.
        (await setup.CreateTaxTableAsync(companyId, "TERM", new DateOnly(2026, 1, 1), null, [new TaxBandInput(0m, 5_000m, 0m), new TaxBandInput(5_000m, null, 20m)])).IsSuccess.Should().BeTrue();
        TerminationRequest banded = Request("TM-001", TerminationType.Retrenchment) with { TerminalTaxTableCode = "term" };
        banded = banded with { Policy = banded.Policy with { TaxFreeAmount = 1_000m } };
        (await terminations.PreviewAsync(banded)).Value.TerminalTax.Should().Be(461.54m);
        (await terminations.PreviewAsync(Request("TM-001", TerminationType.Retrenchment) with { TerminalTaxTableCode = "NONE" })).Error.Code.Should().Be("Payroll.TerminalTaxTable");

        var doneResult = await terminations.TerminateAsync(Request("TM-001", TerminationType.Retrenchment));
        doneResult.IsSuccess.Should().BeTrue(doneResult.IsFailure ? doneResult.Error.ToString() : null);
        TerminationResult done = doneResult.Value;
        done.JournalNumber.Should().NotBeNull();
        List<JournalLine> lines = await (from l in db.Set<JournalLine>().AsNoTracking() join j in db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                                         where j.CompanyId == companyId && j.JournalNumber == done.JournalNumber select l).ToListAsync();
        lines.Sum(l => l.BaseDebit).Should().Be(lines.Sum(l => l.BaseCredit));
        lines.Single(l => l.AccountCode == "6150").BaseDebit.Should().Be(8_307.69m);
        lines.Single(l => l.AccountCode == "1350").BaseCredit.Should().Be(200m);
        lines.Single(l => l.AccountCode == "2420").BaseCredit.Should().Be(8_631.47m);
        (await db.EmployeeLoans.AsNoTracking().SingleAsync(l => l.Reference == "LN-1" && l.CompanyId == companyId)).Status.Should().Be(EmployeeLoanStatus.Settled);
        Employee gone = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == retrenched);
        (gone.Status, gone.TerminationDate).Should().Be((EmployeeStatus.Terminated, new DateOnly(2026, 4, 15)));
        (await terminations.TerminateAsync(Request("TM-001", TerminationType.Retrenchment))).Error.Code.Should().Be("Payroll.AlreadyTerminated");
        string slip = (await terminations.FinalPaySlipAsync(companyId, "TM-001")).Value;
        slip.Should().Contain("FINAL PAY SLIP").And.Contain("Severance").And.Contain("5,538.46").And.Contain("Terminal benefits tax").And.Contain("830.77")
            .And.Contain("8,631.47").And.Contain("Not yet paid");
        (await terminations.FinalPaySlipAsync(companyId, "NOBODY")).IsFailure.Should().BeTrue();

        // The final pay is on the PAYE return for April and on the leaver's tax certificate: 509.09 PAYE and 830.77 terminal tax.
        await scope.ServiceProvider.GetRequiredService<One4All.Infrastructure.Persistence.Seeding.ReportCatalogueSeeder>().SeedAsync();
        One4All.Application.Abstractions.Reporting.IReportRunner runner = scope.ServiceProvider.GetRequiredService<One4All.Application.Abstractions.Reporting.IReportRunner>();
        async Task<string> Csv(string code) => System.Text.Encoding.UTF8.GetString((await runner.RenderAsync(companyId, code, new Dictionary<string, object?>
        {
            ["FromDate"] = new DateOnly(2026, 4, 1), ["ToDate"] = new DateOnly(2026, 4, 30),
        }, One4All.Application.Abstractions.Reporting.ReportFormat.Csv)).Value.Content);
        (await Csv("PR27")).Should().Contain("2026-04,").And.Contain(",1339.86");
        (await Csv("PR28")).Should().Contain("TM-001 Tafadzwa Ncube").And.Contain(",1339.86");

        // Clearance and certificate.
        done.Clearance.Should().Contain(i => i.Description.StartsWith("Final pay calculated", StringComparison.Ordinal) && i.IsCleared);
        ClearanceItem property = done.Clearance.Single(i => i.Description.StartsWith("Company property", StringComparison.Ordinal));
        property.IsCleared.Should().BeFalse();
        (await terminations.ReleaseFinalPayAsync(companyId, "TM-001", new DateOnly(2026, 4, 16), "1000")).Error.Code.Should().Be("Payroll.ClearanceOutstanding", "property is still out");
        (await terminations.ClearItemAsync(companyId, property.Id, 5, new DateOnly(2026, 4, 16))).IsSuccess.Should().BeTrue();
        string? payment = (await terminations.ReleaseFinalPayAsync(companyId, "TM-001", new DateOnly(2026, 4, 17), "1000")).Value;
        List<JournalLine> paid = await (from l in db.Set<JournalLine>().AsNoTracking() join j in db.Journals.AsNoTracking() on l.JournalEntryId equals j.Id
                                        where j.CompanyId == companyId && j.JournalNumber == payment select l).ToListAsync();
        (paid.Single(l => l.AccountCode == "2420").BaseDebit, paid.Single(l => l.AccountCode == "1000").BaseCredit).Should().Be((8_631.47m, 8_631.47m));
        (await terminations.ReleaseFinalPayAsync(companyId, "TM-001", new DateOnly(2026, 4, 17), "1000")).Error.Code.Should().Be("Payroll.FinalPayReleased");
        (await terminations.ClearanceAsync(companyId, "tm-001")).Value.Single(i => i.Id == property.Id).IsCleared.Should().BeTrue();
        string certificate = (await terminations.CertificateOfServiceAsync(companyId, "TM-001")).Value;
        certificate.Should().Contain("CERTIFICATE OF SERVICE").And.Contain("Tafadzwa Ncube").And.Contain("15 January 2020").And.Contain("15 April 2026").And.Contain("retrenchment")
            .And.NotContain("1,000");

        // Resignation: earned salary only (no leave here), and a loan bigger than the pay stays owed.
        FinalPayStatement resignation = (await terminations.TerminateAsync(Request("TM-002", TerminationType.Resignation))).Value.Statement;
        (resignation.Notice, resignation.Gratuity, resignation.Severance, resignation.ProRataSalary).Should().Be((0m, 0m, 0m, 150m));
        (resignation.Recoveries, resignation.UnrecoveredLoans, resignation.Net).Should().Be((140m, 760m, 0m));

        // The next run pays only the one still employed.
        var run = await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-04", new DateOnly(2026, 4, 25), "2420", "TM-2026-04");
        run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
        (await db.Payslips.AsNoTracking().Where(p => p.CompanyId == companyId).Select(p => p.EmployeeId).ToListAsync()).Should().NotContain([retrenched, resigning]).And.HaveCount(1);
        (await terminations.CertificateOfServiceAsync(companyId, "TM-003")).Error.Code.Should().Be("Payroll.NotTerminated");
    }
}
