using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Security;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers soft close (brief §3.4: "soft close… allow adjustments with permission, then hard close"): a closed period refuses ordinary postings but
/// takes an adjustment from a user granted GeneralLedger.PeriodAdjustment; a permanently closed period takes nothing, whoever posts.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class SoftClosedPeriodTests
{
    private readonly AccountingFixture _fixture;

    public SoftClosedPeriodTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_closed_period_takes_only_permitted_adjustments_and_a_hard_closed_one_nothing()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        long yearId = await db.FiscalYears.Where(y => y.CompanyId == companyId).Select(y => y.Id).SingleAsync();
        string tag = Guid.NewGuid().ToString("N")[..8];

        Role adjuster = new($"Adjuster {tag}");
        adjuster.Grant("GeneralLedger.PeriodAdjustment", OperationFlags.View | OperationFlags.Post);
        Role clerk = new($"Clerk {tag}");
        clerk.Grant("GeneralLedger.Journal", OperationFlags.View | OperationFlags.Post);
        db.Roles.AddRange(adjuster, clerk);
        await db.SaveChangesAsync();
        User accountant = new($"acct.{tag}", "Accountant");
        accountant.SetPasswordHash("hash", mustChange: false);
        accountant.AssignRole(adjuster.Id);
        User junior = new($"junior.{tag}", "Junior");
        junior.SetPasswordHash("hash", mustChange: false);
        junior.AssignRole(clerk.Id);
        db.Users.AddRange(accountant, junior);
        await db.SaveChangesAsync();

        IPeriodService periods = scope.ServiceProvider.GetRequiredService<IPeriodService>();
        (await periods.SetStatusAsync(companyId, yearId, 3, "GL", PeriodStatus.Closed)).IsSuccess.Should().BeTrue();
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        Task<Core.Common.Result<PostingReceipt>> Post(long? by, int month) => engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, month, 20), TransactionCurrency = Scenario.Usd, EnforceManualJournalRules = false,
            PostedByUserId = by, Narrative = "March accrual adjustment",
            Lines = [new JournalLineRequest { AccountCode = "6800", Side = JournalSide.Debit, TransactionAmount = 40m }, new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Credit, TransactionAmount = 40m }],
        });

        (await Post(null, 3)).Error.Code.Should().Be("Posting.PeriodNotOpen");
        (await Post(junior.Id, 3)).Error.Code.Should().Be("Posting.PeriodNotOpen", "a user without the adjustment permission");
        (await Post(accountant.Id, 3)).IsSuccess.Should().BeTrue("an adjustment by a permitted user");
        (await Post(junior.Id, 4)).IsSuccess.Should().BeTrue("open periods are unaffected");

        (await periods.SetStatusAsync(companyId, yearId, 3, "GL", PeriodStatus.PermanentlyClosed)).IsSuccess.Should().BeTrue();
        (await Post(accountant.Id, 3)).Error.Code.Should().Be("Posting.PeriodNotOpen", "a hard-closed period takes nothing");
    }
}
