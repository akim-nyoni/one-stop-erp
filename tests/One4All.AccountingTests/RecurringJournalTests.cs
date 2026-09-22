using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RecurringJournalTests
{
    private readonly AccountingFixture _fixture;

    public RecurringJournalTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_recurring_template_posts_a_real_journal_each_time_it_is_run()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long templateId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            IRecurringJournalService rec = scope.ServiceProvider.GetRequiredService<IRecurringJournalService>();

            // An unbalanced template is refused.
            (await rec.CreateAsync(companyId, "RENT", "Monthly rent", "GL",
                [new RecurringJournalLineInput("6300", true, 1000m)])).IsFailure.Should().BeTrue();

            // Monthly rent: Dr rent expense 1000 / Cr bank 1000.
            var created = await rec.CreateAsync(companyId, "RENT", "Monthly rent", "GL",
                [new RecurringJournalLineInput("6300", true, 1000m), new RecurringJournalLineInput("1100", false, 1000m)],
                narrative: "Office rent");
            created.IsSuccess.Should().BeTrue(created.IsFailure ? created.Error.ToString() : null);
            templateId = created.Value;

            // Duplicate code is refused.
            (await rec.CreateAsync(companyId, "rent", "again", "GL",
                [new RecurringJournalLineInput("6300", true, 1m), new RecurringJournalLineInput("1100", false, 1m)])).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IRecurringJournalService rec = scope.ServiceProvider.GetRequiredService<IRecurringJournalService>();
            // Post it for March and April.
            (await rec.PostAsync(companyId, templateId, new DateOnly(2026, 3, 31))).IsSuccess.Should().BeTrue();
            (await rec.PostAsync(companyId, templateId, new DateOnly(2026, 4, 30))).IsSuccess.Should().BeTrue();

            // Deactivate → further posts are refused.
            (await rec.SetActiveAsync(companyId, templateId, false)).IsSuccess.Should().BeTrue();
            (await rec.PostAsync(companyId, templateId, new DateOnly(2026, 5, 31))).IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IGeneralLedgerReports gl = scope.ServiceProvider.GetRequiredService<IGeneralLedgerReports>();
            TrialBalanceReport tb = await gl.TrialBalanceAsync(companyId, new DateOnly(2026, 4, 30));
            tb.IsBalanced.Should().BeTrue();
            tb.Rows.Should().Contain(r => r.AccountCode == "6300" && r.Debit == 2000m);   // two months of rent expense
            tb.Rows.Should().Contain(r => r.AccountCode == "1100" && r.Credit == 2000m);  // bank paid out
        }
    }
}
