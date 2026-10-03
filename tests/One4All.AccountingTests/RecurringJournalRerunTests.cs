using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the recurring-journal guard: a month's run repeated posts nothing more, a single template cannot be
/// posted again for the same or an earlier month, and a run that fails part-way can be re-run after the cause is
/// fixed, posting only the templates that did not post.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RecurringJournalRerunTests
{
    private readonly AccountingFixture _fixture;

    public RecurringJournalRerunTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Rerunning_a_month_never_posts_a_template_twice()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long rentId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            IRecurringJournalService rec = scope.ServiceProvider.GetRequiredService<IRecurringJournalService>();
            rentId = (await rec.CreateAsync(companyId, "RENT", "Rent", "GL",
                [new RecurringJournalLineInput("6300", true, 1000m), new RecurringJournalLineInput("1100", false, 1000m)])).Value;
            await rec.CreateAsync(companyId, "INSUR", "Insurance", "GL",
                [new RecurringJournalLineInput("6300", true, 250m), new RecurringJournalLineInput("1100", false, 250m)]);
        }

        DateOnly march = new(2026, 3, 31);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRecurringJournalService rec = scope.ServiceProvider.GetRequiredService<IRecurringJournalService>();
            (await rec.PostAllActiveAsync(companyId, march)).Value.Should().OnlyContain(l => !l.AlreadyPosted && l.JournalNumber != string.Empty);

            // The same month again: both skipped, nothing posted.
            IReadOnlyList<RecurringRunLine> again = (await rec.PostAllActiveAsync(companyId, march)).Value;
            again.Should().HaveCount(2).And.OnlyContain(l => l.AlreadyPosted && l.JournalNumber == string.Empty);

            // A single post for the same month, or an earlier one, is refused.
            Result<string> sameMonth = await rec.PostAsync(companyId, rentId, new DateOnly(2026, 3, 15));
            sameMonth.Error.Code.Should().Be("Gl.RecurringAlreadyPosted");
            (await rec.PostAsync(companyId, rentId, new DateOnly(2026, 2, 28))).Error.Code.Should().Be("Gl.RecurringAlreadyPosted");
        }

        // A template whose posting fails (its credit account does not exist yet) stops April's run after the others.
        DateOnly april = new(2026, 4, 30);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRecurringJournalService rec = scope.ServiceProvider.GetRequiredService<IRecurringJournalService>();
            await rec.CreateAsync(companyId, "ZACCR", "Accrued audit fee", "GL",
                [new RecurringJournalLineInput("6300", true, 50m), new RecurringJournalLineInput("2950", false, 50m)]);

            Result<IReadOnlyList<RecurringRunLine>> failed = await rec.PostAllActiveAsync(companyId, april);
            failed.Error.Code.Should().Be("Posting.AccountNotFound");

            // The failed template is not left marked as posted.
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Set<RecurringJournal>().AsNoTracking().SingleAsync(r => r.CompanyId == companyId && r.Code == "ZACCR")).LastPostedPeriod.Should().BeNull();

            db.Accounts.Add(new LedgerAccount(companyId, "2950", "Accruals", AccountType.Liability));
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            // The re-run posts only the template that failed.
            IRecurringJournalService rec = scope.ServiceProvider.GetRequiredService<IRecurringJournalService>();
            IReadOnlyList<RecurringRunLine> rerun = (await rec.PostAllActiveAsync(companyId, april)).Value;
            rerun.Single(l => l.Code == "ZACCR").AlreadyPosted.Should().BeFalse();
            rerun.Where(l => l.Code != "ZACCR").Should().HaveCount(2).And.OnlyContain(l => l.AlreadyPosted);

            ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], april)).Value;
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Single(r => r.AccountCode == "6300").Debit.Should().Be(2550m); // (1000 + 250) × 2 months + 50 once
            tb.Rows.Single(r => r.AccountCode == "2950").Credit.Should().Be(50m);
        }
    }
}
