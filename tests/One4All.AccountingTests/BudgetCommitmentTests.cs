using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Budgeting;
using One4All.Core.Budgeting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers commitment control (encumbrances): an open commitment reduces the available budget so funds can't
/// be committed twice, and releasing it restores the availability.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetCommitmentTests
{
    private readonly AccountingFixture _fixture;

    public BudgetCommitmentTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Open_commitments_reduce_availability_and_release_restores_it()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long fiscalYearId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            fiscalYearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();
        }

        // Actual spend of 500 on Office Supplies (6800) in January, budget 1000.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine posting = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            await posting.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 1, 15),
                TransactionCurrency = Scenario.Usd, DocumentType = "JNL", DocumentNumber = "ACT-1",
                Lines =
                [
                    new JournalLineRequest { AccountCode = "6800", Side = JournalSide.Debit, TransactionAmount = 500m },
                    new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 500m },
                ],
            });

            IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
            long budgetId = (await budgeting.CreateBudgetAsync(companyId, fiscalYearId, "2026", BudgetType.OriginalApproved, "ADMIN", [new NewBudgetLine("6800", 1, 1000m)])).Value;
            await budgeting.ActivateAsync(companyId, budgetId);
        }

        long commitmentId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();

            // Before any commitment: available = 1000 − 500 = 500.
            var before = await budgeting.CheckAvailabilityAsync(companyId, fiscalYearId, BudgetType.OriginalApproved, "ADMIN", "6800", 300m, 1);
            before.Value.Committed.Should().Be(0m);
            before.Value.Available.Should().Be(500m);
            before.Value.Passes.Should().BeTrue();

            // Commit 300 (a purchase order): available drops to 200.
            commitmentId = (await budgeting.CommitAsync(companyId, fiscalYearId, BudgetType.OriginalApproved, "ADMIN", "6800", 1, 300m, "PO-1")).Value;

            var committed = await budgeting.CheckAvailabilityAsync(companyId, fiscalYearId, BudgetType.OriginalApproved, "ADMIN", "6800", 300m, 1);
            committed.Value.Committed.Should().Be(300m);
            committed.Value.Available.Should().Be(200m);
            committed.Value.Passes.Should().BeFalse(); // 300 no longer fits within 200
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();

            // Release the commitment (the order was invoiced/cancelled): availability restored to 500.
            (await budgeting.ReleaseCommitmentAsync(companyId, commitmentId)).IsSuccess.Should().BeTrue();

            var after = await budgeting.CheckAvailabilityAsync(companyId, fiscalYearId, BudgetType.OriginalApproved, "ADMIN", "6800", 300m, 1);
            after.Value.Committed.Should().Be(0m);
            after.Value.Available.Should().Be(500m);
            after.Value.Passes.Should().BeTrue();

            // Releasing again is refused.
            (await budgeting.ReleaseCommitmentAsync(companyId, commitmentId)).Error.Code.Should().Be("Budget.Commit.AlreadyReleased");
        }
    }
}
