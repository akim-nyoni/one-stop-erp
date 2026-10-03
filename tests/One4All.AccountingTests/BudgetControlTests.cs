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
/// Covers virements and the vote book: office supplies (6800) has 1,000 approved, 300 spent and 100 committed, so a
/// virement of 700 to travel (6700) is refused while one of 400 is accepted; its requester cannot approve it; once
/// approved the register shows 6800 at 600 and 6700 at 900, and the vote book adds a supplementary 200 for 6800,
/// leaving 400 unexpended there and 900 on travel.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class BudgetControlTests
{
    private const long Clerk = 31;
    private const long Treasurer = 32;

    private readonly AccountingFixture _fixture;

    public BudgetControlTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Virements_move_budget_with_approval_and_the_vote_book_reconciles()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).FirstAsync();

        IBudgetingService budgeting = scope.ServiceProvider.GetRequiredService<IBudgetingService>();
        long original = (await budgeting.CreateBudgetAsync(companyId, yearId, "2026", BudgetType.OriginalApproved, null,
            [new NewBudgetLine("6800", 3, 1_000m), new NewBudgetLine("6700", 3, 500m)])).Value;
        (await budgeting.ActivateAsync(companyId, original)).IsSuccess.Should().BeTrue();
        long supplementary = (await budgeting.CreateBudgetAsync(companyId, yearId, "2026 supplementary", BudgetType.SupplementaryRevised, null, [new NewBudgetLine("6800", 6, 200m)])).Value;
        (await budgeting.ActivateAsync(companyId, supplementary)).IsSuccess.Should().BeTrue();

        (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd,
            Lines =
            [
                new JournalLineRequest { AccountCode = "6800", Side = JournalSide.Debit, TransactionAmount = 300m },
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 300m },
            ],
        })).IsSuccess.Should().BeTrue();
        (await budgeting.CommitAsync(companyId, yearId, BudgetType.OriginalApproved, null, "6800", 3, 100m, "PO-77")).IsSuccess.Should().BeTrue();

        IBudgetControlService control = scope.ServiceProvider.GetRequiredService<IBudgetControlService>();
        (await control.RequestVirementAsync(companyId, original, new VirementRequest("6800", "6700", 3, 700m, "Conference travel"), Clerk))
            .Error.Code.Should().Be("Budget.Virement.ExceedsAvailable");
        (await control.RequestVirementAsync(companyId, original, new VirementRequest("6800", "6800", 3, 10m, "Nothing"), Clerk))
            .Error.Code.Should().Be("Budget.Virement.SameLine");
        long virement = (await control.RequestVirementAsync(companyId, original, new VirementRequest("6800", "6700", 3, 400m, "Conference travel"), Clerk)).Value;
        (await control.ApproveVirementAsync(companyId, virement, Clerk)).Error.Code.Should().Be("Budget.Virement.SelfApproval");
        (await control.ApproveVirementAsync(companyId, virement, Treasurer)).IsSuccess.Should().BeTrue();
        (await control.ApproveVirementAsync(companyId, virement, Treasurer)).Error.Code.Should().Be("Budget.Virement.NotPending");

        (await control.VirementRegisterAsync(companyId, original)).Value.Should().Equal(
            new VirementRegisterRow("6700", 500m, 400m, 0m),
            new VirementRegisterRow("6800", 1_000m, 0m, 400m));

        IReadOnlyList<VoteBookRow> votes = await control.VoteBookAsync(companyId, yearId, 12);
        votes.Select(v => (v.AccountCode, v.Original, v.Supplementary, v.Virements, v.Expenditure, v.Commitments, v.Unexpended)).Should().Equal(
            ("6700", 500m, 0m, 400m, 0m, 0m, 900m),
            ("6800", 1_000m, 200m, -400m, 300m, 100m, 400m));

        long rejected = (await control.RequestVirementAsync(companyId, original, new VirementRequest("6700", "6800", 3, 50m, "Undo"), Clerk)).Value;
        (await control.RejectVirementAsync(companyId, rejected, Treasurer, "")).Error.Code.Should().Be("Budget.Virement.ReasonRequired");
        (await control.RejectVirementAsync(companyId, rejected, Treasurer, "Not needed")).IsSuccess.Should().BeTrue();
        (await control.VirementRegisterAsync(companyId, original)).Value.Sum(r => r.Current).Should().Be(1_500m);
    }
}
