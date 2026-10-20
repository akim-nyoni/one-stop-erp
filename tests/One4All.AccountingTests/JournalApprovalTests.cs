using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers maker-checker approval of manual journals: above the threshold, a manual journal cannot post directly; a
/// submitted journal is numbered and held, cannot be released by the deferred-journal run or approved by its preparer,
/// and posts when a second user approves it. A rejected journal is cancelled, cannot be reversed, and keeps its number
/// so the sequence shows no gap. The database itself refuses to post a held journal without an approver.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class JournalApprovalTests
{
    private const long Preparer = 101;
    private const long Approver = 202;

    private readonly AccountingFixture _fixture;

    public JournalApprovalTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Manual_journals_post_only_after_a_second_user_approves_them()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Company company = await db.Companies.SingleAsync(c => c.Id == companyId);
        company.ManualJournalApprovalThreshold = 50m;
        await db.SaveChangesAsync();

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        IJournalApprovalService approvals = scope.ServiceProvider.GetRequiredService<IJournalApprovalService>();

        (await engine.PostAsync(Manual(companyId, "Direct"))).Error.Code.Should().Be("Posting.ApprovalRequired");
        (await engine.PostAsync(Manual(companyId, "Small", 50m))).IsSuccess.Should().BeTrue(); // at the threshold, no approval needed

        // Submitted: numbered and held, out of reach of the deferred-journal run and of its preparer.
        PostingReceipt first = (await approvals.SubmitAsync(Manual(companyId, "Accrue rent"), Preparer)).Value;
        first.Deferred.Should().BeTrue();
        (await approvals.AwaitingApprovalAsync(companyId)).Should().ContainSingle()
            .Which.Should().Be(new JournalAwaitingApproval(first.JournalId, first.JournalNumber, new DateOnly(2026, 3, 10), "Accrue rent", 100m, Preparer));
        (await engine.ReleaseAllDeferredAsync(companyId, new DateOnly(2026, 12, 31))).Released.Should().BeEmpty();
        (await engine.ReleaseDeferredAsync(companyId, first.JournalId)).Error.Code.Should().Be("Posting.AwaitingApproval");
        (await approvals.ApproveAsync(companyId, first.JournalId, Preparer)).Error.Code.Should().Be("Gl.SelfApproval");

        PostingReceipt posted = (await approvals.ApproveAsync(companyId, first.JournalId, Approver)).Value;
        posted.JournalNumber.Should().Be(first.JournalNumber);
        JournalEntry approved = await db.Journals.AsNoTracking().SingleAsync(j => j.Id == first.JournalId);
        (approved.Status, approved.PreparedByUserId, approved.ApprovedByUserId, approved.AwaitingApproval).Should().Be((JournalStatus.Posted, Preparer, Approver, false));

        // Rejected: cancelled, never posts, cannot be reversed or approved later, and leaves no numbering gap.
        PostingReceipt second = (await approvals.SubmitAsync(Manual(companyId, "Duplicate accrual"), Preparer)).Value;
        (await approvals.RejectAsync(companyId, second.JournalId, " ")).Error.Code.Should().Be("Gl.ReasonRequired");
        (await approvals.RejectAsync(companyId, second.JournalId, "Already accrued")).IsSuccess.Should().BeTrue();
        JournalEntry rejected = await db.Journals.AsNoTracking().SingleAsync(j => j.Id == second.JournalId);
        (rejected.Status, rejected.RejectionReason).Should().Be((JournalStatus.Cancelled, "Already accrued"));
        (await engine.ReverseAsync(companyId, second.JournalId, new DateOnly(2026, 3, 31), "Undo")).Error.Code.Should().Be("Gl.NotPosted");
        (await approvals.ApproveAsync(companyId, second.JournalId, Approver)).Error.Code.Should().Be("Gl.NotAwaitingApproval");

        // The database refuses to post a held journal that nobody approved.
        PostingReceipt third = (await approvals.SubmitAsync(Manual(companyId, "Held"), Preparer)).Value;
        Func<Task> bypass = () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE gl.journal SET status = 2, awaiting_approval = false WHERE id = {third.JournalId}");
        (await bypass.Should().ThrowAsync<Npgsql.PostgresException>()).Which.ConstraintName.Should().Be("ck_journal_approved_before_posting");

        // An accrual names its reverse-on date: approved, it posts and reverses itself on that date.
        (await approvals.SubmitAsync(Manual(companyId, "Bad accrual", reverseOn: new DateOnly(2026, 3, 10)), Preparer)).Error.Code.Should().Be("Posting.ReverseOn");
        PostingReceipt accrual = (await approvals.SubmitAsync(Manual(companyId, "Accrue audit fee", reverseOn: new DateOnly(2026, 4, 1)), Preparer)).Value;
        (await approvals.ApproveAsync(companyId, accrual.JournalId, Approver)).IsSuccess.Should().BeTrue();
        (await db.Journals.AsNoTracking().SingleAsync(j => j.CompanyId == companyId && j.ReversalOfJournalId == accrual.JournalId)).PostingDate.Should().Be(new DateOnly(2026, 4, 1));

        LedgerIntegrityReport integrity = (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value;
        integrity.Issues.Should().NotContain(i => i.Check == IntegrityCheck.JournalNumberGap);

        ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
        tb.IsBalanced.Should().BeTrue();
        tb.Rows.Single(r => r.AccountCode == "6000").Debit.Should().Be(250m); // 50 + 100 approved + the 100 accrual (reversed in April)
    }

    private static JournalRequest Manual(long companyId, string narrative, decimal amount = 100m, DateOnly? reverseOn = null) => new()
    {
        CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd,
        EnforceManualJournalRules = true, Narrative = narrative, ReverseOn = reverseOn,
        Lines =
        [
            new JournalLineRequest { AccountCode = "6000", Side = JournalSide.Debit, TransactionAmount = amount },
            new JournalLineRequest { AccountCode = "2300", Side = JournalSide.Credit, TransactionAmount = amount },
        ],
    };
}
