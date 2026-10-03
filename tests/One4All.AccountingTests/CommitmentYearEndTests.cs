using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Budgeting;
using One4All.Core.Budgeting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers year-end commitments: of 2026's open commitments, the 100 order chosen is carried into 2027 as a new open
/// commitment linked to it, the 50 order lapses, and a commitment already released is left alone. A commitment not open
/// in the year cannot be carried, and a second run has nothing left to decide.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CommitmentYearEndTests
{
    private readonly AccountingFixture _fixture;

    public CommitmentYearEndTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Open_commitments_lapse_or_carry_forward_at_year_end()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long year2026 = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        FiscalYear next = new(companyId, "2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));
        db.FiscalYears.Add(next);
        await db.SaveChangesAsync();

        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long carried = (await budgeting.CommitAsync(companyId, year2026, BudgetType.OriginalApproved, null, "6800", 11, 100m, "PO-100")).Value;
        long lapsed = (await budgeting.CommitAsync(companyId, year2026, BudgetType.OriginalApproved, null, "6700", 12, 50m, "PO-101")).Value;
        long released = (await budgeting.CommitAsync(companyId, year2026, BudgetType.OriginalApproved, null, "6700", 6, 20m, "PO-102")).Value;
        (await budgeting.ReleaseCommitmentAsync(companyId, released)).IsSuccess.Should().BeTrue();

        IBudgetControlService control = scope.ServiceProvider.GetRequiredService<IBudgetControlService>();
        (await control.CloseYearCommitmentsAsync(companyId, year2026, next.Id, [released], 9)).Error.Code.Should().Be("Budget.YearEnd.NotOpen");
        (await control.CloseYearCommitmentsAsync(companyId, next.Id, year2026, [], 9)).Error.Code.Should().Be("Budget.YearEnd.Years");

        CommitmentYearEnd result = (await control.CloseYearCommitmentsAsync(companyId, year2026, next.Id, [carried], 9)).Value;
        result.Lapsed.Should().ContainSingle().Which.Should().BeEquivalentTo(new { CommitmentId = lapsed, Reference = "PO-101", Amount = 50m, CarriedToId = (long?)null });
        YearEndCommitment forward = result.CarriedForward.Should().ContainSingle().Subject;
        forward.CommitmentId.Should().Be(carried);

        List<BudgetCommitment> all = await db.Set<BudgetCommitment>().AsNoTracking().Where(c => c.CompanyId == companyId).ToListAsync();
        all.Single(c => c.Id == carried).Status.Should().Be(CommitmentStatus.CarriedForward);
        all.Single(c => c.Id == lapsed).Should().BeEquivalentTo(new { Status = CommitmentStatus.Lapsed, YearEndApprovedByUserId = 9L });
        all.Single(c => c.Id == released).Status.Should().Be(CommitmentStatus.Released);
        all.Single(c => c.Id == forward.CarriedToId).Should().BeEquivalentTo(new
        {
            FiscalYearId = next.Id, Status = CommitmentStatus.Open, AccountCode = "6800", Period = 1, Amount = 100m, Reference = "PO-100", CarriedFromId = (long?)carried,
        });

        CommitmentYearEnd again = (await control.CloseYearCommitmentsAsync(companyId, year2026, next.Id, [], 9)).Value;
        (again.Lapsed.Count, again.CarriedForward.Count).Should().Be((0, 0));
    }
}
