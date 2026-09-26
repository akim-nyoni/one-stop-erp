using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class RecurringJournalReportTests
{
    private readonly AccountingFixture _fixture;

    public RecurringJournalReportTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Recurring_journal_register_lists_templates()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);

            IRecurringJournalService svc = scope.ServiceProvider.GetRequiredService<IRecurringJournalService>();
            var created = await svc.CreateAsync(companyId, "RJ-RENT", "Monthly rent accrual", "GL",
                [new RecurringJournalLineInput("6100", true, 800m), new RecurringJournalLineInput("2470", false, 800m)]);
            created.IsSuccess.Should().BeTrue(created.IsFailure ? created.Error.ToString() : null);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IReportRunner runner = scope.ServiceProvider.GetRequiredService<IReportRunner>();
            string html = Encoding.UTF8.GetString((await runner.RenderAsync(companyId, "GL-RECUR", new Dictionary<string, object?>(), ReportFormat.Html)).Value.Content);
            html.Should().Contain("RJ-RENT").And.Contain("Monthly rent accrual").And.Contain("800.00").And.Contain("Yes");
        }
    }
}
