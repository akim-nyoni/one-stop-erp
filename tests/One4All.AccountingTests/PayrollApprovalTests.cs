using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Payroll;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers maker-checker payroll: with approval required a direct run is refused; a calculation is approved by someone else (and by a
/// second, different approver when net pay moves past the threshold), shows the exception report, and is committed only while it
/// still matches what was approved; PR7 shows who calculated, approved and committed.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollApprovalTests
{
    private readonly AccountingFixture _fixture;

    public PayrollApprovalTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Payroll_is_calculated_approved_and_committed()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        long companyId = await Scenario.SeedAsync(db);
        Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.PayrollApprovalRequired = true;
        company.PayrollSecondApprovalVariancePercent = 20m;
        User clerk = new($"pa-clerk-{companyId}", "Payroll Clerk"), boss = new($"pa-boss1-{companyId}", "Finance Manager"), director = new($"pa-boss2-{companyId}", "Director");
        db.Users.AddRange(clerk, boss, director);
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        db.PayElements.Add(basic);
        await db.SaveChangesAsync();
        Employee ann = new(companyId, "PA-1", "Ann First", 1000m, new DateOnly(2025, 1, 1)) { NationalIdNumber = $"PA1-{companyId}", TaxNumber = "T1", BankAccountNumber = $"PA1{companyId}" };
        Employee ben = new(companyId, "PA-2", "Ben Second", 2000m, new DateOnly(2025, 1, 1)) { NationalIdNumber = $"PA2-{companyId}", TaxNumber = "T2", BankAccountNumber = $"PA2{companyId}" };
        ann.AssignPayElement(basic.Id);
        ben.AssignPayElement(basic.Id);
        db.Employees.AddRange(ann, ben);
        await db.SaveChangesAsync();

        IPayrollService payroll = scope.ServiceProvider.GetRequiredService<IPayrollService>();
        IPayrollApprovalService approval = scope.ServiceProvider.GetRequiredService<IPayrollApprovalService>();
        (await payroll.RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "PA-J01")).Error.Code.Should().Be("Payroll.ApprovalRequired");
        PayrollDraftRequest Draft(int month) => new()
        {
            CompanyId = companyId, PeriodLabel = $"2026-{month:00}", PayDate = new DateOnly(2026, month, 25), NetPayAccountCode = "2420", DocumentNumber = $"PA-J{month:00}", UserId = clerk.Id,
        };

        // January: one approver, who cannot be the clerk; nothing commits before approval.
        PayrollDraftResult january = (await approval.CalculateRunAsync(Draft(1))).Value;
        january.Should().Match<PayrollDraftResult>(d => d.Employees == 2 && d.Net == 3000m && d.PreviousNet == null && !d.RequiresSecondApproval && d.Exceptions.Count == 0);
        (await approval.ApproveRunAsync(companyId, january.DraftId, clerk.Id)).Error.Code.Should().Be("Payroll.SameApprover");
        (await approval.CommitRunAsync(companyId, january.DraftId, clerk.Id)).Error.Code.Should().Be("Payroll.NotApproved");
        (await approval.ApproveRunAsync(companyId, january.DraftId, boss.Id)).Value.Status.Should().Be(PayrollDraftStatus.Approved);
        (await approval.CommitRunAsync(companyId, january.DraftId, clerk.Id)).Value.TotalNet.Should().Be(3000m);
        (await approval.ApproveRunAsync(companyId, january.DraftId, director.Id)).Error.Code.Should().Be("Payroll.DraftNotPending");

        // February: Ben's raise moves net pay 33% — two different approvers, and the exception report says why.
        ben.BasicSalary = 3000m;
        await db.SaveChangesAsync();
        PayrollDraftResult february = (await approval.CalculateRunAsync(Draft(2))).Value;
        february.RequiresSecondApproval.Should().BeTrue();
        february.MovementPercent.Should().Be(33.3m);
        february.Exceptions.Should().Equal("PA-2: net pay 3,000.00 moved 50.0% from 2,000.00");
        (await approval.ApproveRunAsync(companyId, february.DraftId, boss.Id)).Value.Status.Should().Be(PayrollDraftStatus.Calculated);
        (await approval.ApproveRunAsync(companyId, february.DraftId, boss.Id)).Error.Code.Should().Be("Payroll.SameApprover");
        (await approval.ApproveRunAsync(companyId, february.DraftId, director.Id)).Value.Status.Should().Be(PayrollDraftStatus.Approved);

        // A change after approval stops the commit; recalculating supersedes the old draft and starts approval again.
        ann.BasicSalary = 1100m;
        await db.SaveChangesAsync();
        (await approval.CommitRunAsync(companyId, february.DraftId, clerk.Id)).Error.Code.Should().Be("Payroll.ChangedSinceApproval");
        PayrollDraftResult again = (await approval.CalculateRunAsync(Draft(2))).Value;
        (await approval.CommitRunAsync(companyId, february.DraftId, clerk.Id)).Error.Code.Should().Be("Payroll.NotApproved");
        (await approval.ApproveRunAsync(companyId, again.DraftId, boss.Id)).IsSuccess.Should().BeTrue();
        (await approval.ApproveRunAsync(companyId, again.DraftId, director.Id)).IsSuccess.Should().BeTrue();
        (await approval.CommitRunAsync(companyId, again.DraftId, clerk.Id)).Value.TotalNet.Should().Be(4100m);

        var rendered = await scope.ServiceProvider.GetRequiredService<IReportRunner>().RenderAsync(companyId, "PR7",
            new Dictionary<string, object?> { ["FromDate"] = new DateOnly(2026, 1, 1), ["ToDate"] = new DateOnly(2026, 2, 28) }, ReportFormat.Csv);
        rendered.IsSuccess.Should().BeTrue(rendered.IsFailure ? rendered.Error.Message : null);
        string csv = Encoding.UTF8.GetString(rendered.Value.Content);
        csv.Should().Contain($",pa-clerk-{companyId},pa-boss1-{companyId},3000")
            .And.Contain($",pa-clerk-{companyId},pa-boss1-{companyId} + pa-boss2-{companyId},4100");
    }
}
