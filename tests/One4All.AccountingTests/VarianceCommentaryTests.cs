using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Budgeting;
using One4All.Core.Budgeting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers variance commentary: with 1,000 budgeted for stationery and 500 for travel in March, spending 1,300 and 520
/// makes only stationery material at 10%; it is owed an explanation until one is given, and a revised explanation
/// replaces the first.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class VarianceCommentaryTests
{
    private readonly AccountingFixture _fixture;

    public VarianceCommentaryTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Material_variances_are_listed_with_their_explanations()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();

        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long budgetId = (await budgeting.CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, null,
            [new NewBudgetLine("6800", 3, 1_000m), new NewBudgetLine("6700", 3, 500m)])).Value;
        (await budgeting.ActivateAsync(companyId, budgetId)).IsSuccess.Should().BeTrue();

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        foreach ((string account, decimal amount) in new[] { ("6800", 1_300m), ("6700", 520m) })
        {
            (await engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd,
                Lines =
                [
                    new JournalLineRequest { AccountCode = account, Side = JournalSide.Debit, TransactionAmount = amount },
                    new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = amount },
                ],
            })).IsSuccess.Should().BeTrue();
        }

        IVarianceCommentaryService commentary = scope.ServiceProvider.GetRequiredService<IVarianceCommentaryService>();
        VarianceThreshold threshold = new(1_000m, 10m);
        MaterialVarianceRow owed = (await commentary.MaterialVariancesAsync(companyId, yearId, BudgetType.OriginalApproved, null, 3, threshold)).Value
            .Should().ContainSingle().Subject;
        owed.Should().BeEquivalentTo(new { AccountCode = "6800", Budget = 1_000m, Actual = 1_300m, Variance = -300m, AwaitingComment = true });

        (await commentary.CommentAsync(companyId, yearId, 3, "6800", " ", 4)).Error.Code.Should().Be("Budget.Comment.Invalid");
        (await commentary.CommentAsync(companyId, yearId, 3, "6800", "Annual reports printed early", 4)).IsSuccess.Should().BeTrue();
        (await commentary.CommentAsync(companyId, yearId, 3, "6800", "Annual reports printed early; April budget will absorb it", 5)).IsSuccess.Should().BeTrue();

        MaterialVarianceRow explained = (await commentary.MaterialVariancesAsync(companyId, yearId, BudgetType.OriginalApproved, null, 3, threshold)).Value.Single();
        explained.Should().BeEquivalentTo(new { Comment = "Annual reports printed early; April budget will absorb it", CommentedByUserId = 5L, AwaitingComment = false });
        (await db.Set<VarianceComment>().CountAsync(c => c.CompanyId == companyId)).Should().Be(1);
    }
}
