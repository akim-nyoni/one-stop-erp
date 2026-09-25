using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RecurringJournalBatchTests
{
    private readonly AccountingFixture _fixture;

    public RecurringJournalBatchTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Month_end_run_posts_every_active_template_once()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long inactiveId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            IRecurringJournalService rec = scope.ServiceProvider.GetRequiredService<IRecurringJournalService>();
            await rec.CreateAsync(companyId, "RENT", "Rent", "GL",
                [new RecurringJournalLineInput("6300", true, 1000m), new RecurringJournalLineInput("1100", false, 1000m)]);
            await rec.CreateAsync(companyId, "INSUR", "Insurance", "GL",
                [new RecurringJournalLineInput("6300", true, 250m), new RecurringJournalLineInput("1100", false, 250m)]);
            inactiveId = (await rec.CreateAsync(companyId, "OLD", "Retired charge", "GL",
                [new RecurringJournalLineInput("6300", true, 999m), new RecurringJournalLineInput("1100", false, 999m)])).Value;
            await rec.SetActiveAsync(companyId, inactiveId, false);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRecurringJournalService rec = scope.ServiceProvider.GetRequiredService<IRecurringJournalService>();
            var run = await rec.PostAllActiveAsync(companyId, new DateOnly(2026, 3, 31));
            run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
            run.Value.Should().HaveCount(2); // RENT + INSUR, not the inactive OLD
            run.Value.Select(l => l.Code).Should().BeEquivalentTo(["INSUR", "RENT"]);
            run.Value.Should().OnlyContain(l => !string.IsNullOrEmpty(l.JournalNumber));
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 3, 31));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "6300" && r.Debit == 1250m);  // 1000 rent + 250 insurance (OLD skipped)
            tb.Rows.Should().Contain(r => r.AccountCode == "1100" && r.Credit == 1250m);
        }
    }
}
