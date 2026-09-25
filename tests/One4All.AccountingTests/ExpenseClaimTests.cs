using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Expenses;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Expenses;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class ExpenseClaimTests
{
    private readonly AccountingFixture _fixture;

    public ExpenseClaimTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Approved_claim_reimburses_and_posts_the_expenses_against_the_bank()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long employeeId;
        long claimId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            Employee emp = new(companyId, "E-200", "Tess Traveller", 4_000m, new DateOnly(2025, 1, 1));
            db.Set<Employee>().Add(emp);
            await db.SaveChangesAsync();
            employeeId = emp.Id;

            IExpenseClaimService claims = scope.ServiceProvider.GetRequiredService<IExpenseClaimService>();
            var create = await claims.CreateAsync(companyId, employeeId, "EXP-1", new DateOnly(2026, 3, 5),
            [
                new ExpenseClaimLineRequest("Client lunch", "6700", 120m),
                new ExpenseClaimLineRequest("Printer paper", "6800", 30m),
            ]);
            create.IsSuccess.Should().BeTrue(create.IsFailure ? create.Error.ToString() : null);
            claimId = create.Value;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IExpenseClaimService claims = scope.ServiceProvider.GetRequiredService<IExpenseClaimService>();

            // Cannot reimburse before approval.
            (await claims.ReimburseAsync(companyId, claimId, new DateOnly(2026, 3, 10), "1100", "PAY-1")).IsFailure.Should().BeTrue();

            (await claims.ApproveAsync(companyId, claimId, approvedByUserId: 1)).IsSuccess.Should().BeTrue();

            var reimburse = await claims.ReimburseAsync(companyId, claimId, new DateOnly(2026, 3, 10), "1100", "PAY-1");
            reimburse.IsSuccess.Should().BeTrue(reimburse.IsFailure ? reimburse.Error.ToString() : null);

            // A second reimbursement is refused (no longer approved).
            (await claims.ReimburseAsync(companyId, claimId, new DateOnly(2026, 3, 11), "1100", "PAY-2")).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            ExpenseClaim claim = await db.Set<ExpenseClaim>().Include(c => c.Lines).FirstAsync(c => c.Id == claimId);
            claim.Status.Should().Be(ExpenseClaimStatus.Reimbursed);
            claim.JournalNumber.Should().NotBeNullOrEmpty();
            claim.Total.Should().Be(150m);

            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "6700" && r.Debit == 120m);
            tb.Rows.Should().Contain(r => r.AccountCode == "6800" && r.Debit == 30m);
            // Bank credited by the full 150 reimbursement.
            tb.Rows.Should().Contain(r => r.AccountCode == "1100" && r.Credit == 150m);
        }
    }

    [SkippableFact]
    public async Task Draft_claim_can_be_rejected_and_not_reimbursed()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Employee emp = new(companyId, "E-201", "Rex Rejected", 4_000m, new DateOnly(2025, 1, 1));
        db.Set<Employee>().Add(emp);
        await db.SaveChangesAsync();

        IExpenseClaimService claims = scope.ServiceProvider.GetRequiredService<IExpenseClaimService>();
        var create = await claims.CreateAsync(companyId, emp.Id, "EXP-2", new DateOnly(2026, 3, 5),
            [new ExpenseClaimLineRequest("Dubious taxi", "6700", 500m)]);
        create.IsSuccess.Should().BeTrue();

        (await claims.RejectAsync(companyId, create.Value)).IsSuccess.Should().BeTrue();
        (await claims.ReimburseAsync(companyId, create.Value, new DateOnly(2026, 3, 10), "1100", "PAY-9")).IsFailure.Should().BeTrue();

        ExpenseClaim claim = await db.Set<ExpenseClaim>().FirstAsync(c => c.Id == create.Value);
        claim.Status.Should().Be(ExpenseClaimStatus.Rejected);
    }

    [SkippableFact]
    public async Task Empty_or_non_positive_claims_are_refused()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Employee emp = new(companyId, "E-202", "Nil Claimant", 4_000m, new DateOnly(2025, 1, 1));
        db.Set<Employee>().Add(emp);
        await db.SaveChangesAsync();

        IExpenseClaimService claims = scope.ServiceProvider.GetRequiredService<IExpenseClaimService>();
        (await claims.CreateAsync(companyId, emp.Id, "EXP-3", new DateOnly(2026, 3, 5), [])).IsFailure.Should().BeTrue();
        (await claims.CreateAsync(companyId, emp.Id, "EXP-4", new DateOnly(2026, 3, 5),
            [new ExpenseClaimLineRequest("Bad", "6700", -10m)])).IsFailure.Should().BeTrue();
    }
}
