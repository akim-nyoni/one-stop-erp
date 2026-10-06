using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Accounting;
using One4All.Core.Payroll;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers two payroll controls: the journal analyses pay cost by the employee's department and cost centre (matched to the GL
/// dimensions by code or name), and nobody may calculate, approve or reverse a run that includes their own employee record.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class PayrollAnalysisTests
{
    private readonly AccountingFixture _fixture;

    public PayrollAnalysisTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Pay_cost_is_analysed_and_own_pay_is_blocked()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        db.Departments.AddRange(new Department(companyId, "FIN", "Finance"), new Department(companyId, "OPS", "Operations"));
        db.CostCentres.Add(new CostCentre(companyId, "HQ", "Head Office"));
        User clerk = new($"pa2-clerk-{companyId}", "Clerk"), boss = new($"pa2-boss-{companyId}", "Boss");
        db.Users.AddRange(clerk, boss);
        PayElement basic = new(companyId, "BASIC", "Basic Salary", PayElementType.Earning, CalculationBasis.Formula, 1, "6100") { Formula = "BASIC", Taxable = true };
        db.PayElements.Add(basic);
        await db.SaveChangesAsync();
        Employee ann = new(companyId, "AN-1", "Ann Finance", 1000m, new DateOnly(2025, 1, 1)) { Department = "Finance", CostCentre = "hq", UserId = clerk.Id };
        Employee ben = new(companyId, "AN-2", "Ben Ops", 2000m, new DateOnly(2025, 1, 1)) { Department = "OPS" };
        Employee cid = new(companyId, "AN-3", "Cid Elsewhere", 500m, new DateOnly(2025, 1, 1)) { Department = "Somewhere else" };
        foreach (Employee e in new[] { ann, ben, cid })
        {
            e.AssignPayElement(basic.Id);
        }

        db.Employees.AddRange(ann, ben, cid);
        await db.SaveChangesAsync();

        string journal = (await scope.ServiceProvider.GetRequiredService<IPayrollService>().RunAsync(companyId, "2026-01", new DateOnly(2026, 1, 25), "2420", "AN-01")).Value.JournalNumber;
        var lines = await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.JournalNumber == journal).SelectMany(j => j.Lines)
            .Where(l => l.AccountCode == "6100").Select(l => new { l.Department, l.CostCentre, l.BaseDebit }).ToListAsync();
        lines.Select(l => (l.Department ?? string.Empty, l.CostCentre ?? string.Empty, l.BaseDebit)).Should().BeEquivalentTo([("FIN", "HQ", 1000m), ("OPS", string.Empty, 2000m), (string.Empty, string.Empty, 500m)]);

        // The clerk is on this payroll: they can neither calculate nor approve its next run, nor approve the reversal of this one.
        Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.PayrollApprovalRequired = true;
        await db.SaveChangesAsync();
        IPayrollApprovalService approval = scope.ServiceProvider.GetRequiredService<IPayrollApprovalService>();
        PayrollDraftRequest Draft(long user) => new() { CompanyId = companyId, PeriodLabel = "2026-02", PayDate = new DateOnly(2026, 2, 25), NetPayAccountCode = "2420", DocumentNumber = "AN-02", UserId = user };
        (await approval.CalculateRunAsync(Draft(clerk.Id))).Error.Message.Should().Contain("AN-1");
        long draft = (await approval.CalculateRunAsync(Draft(boss.Id))).Value.DraftId;
        (await approval.ApproveRunAsync(companyId, draft, clerk.Id)).Error.Code.Should().Be("Payroll.OwnPay");
        (await scope.ServiceProvider.GetRequiredService<IPayrollAdjustmentService>().ReverseRunAsync(companyId, "AN-01", new DateOnly(2026, 1, 28), "Test", clerk.Id)).Error.Code.Should().Be("Payroll.OwnPay");
    }
}
