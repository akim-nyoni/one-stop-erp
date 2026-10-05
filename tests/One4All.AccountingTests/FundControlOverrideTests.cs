using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Budgeting;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Persistence.Seeding;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers fund-control blocks and overrides (brief §14 B27): a refused posting is recorded even though it rolls back; an override needs a
/// reason and a supervisor other than the preparer, lets the posting through and is recorded too.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class FundControlOverrideTests
{
    private readonly AccountingFixture _fixture;

    public FundControlOverrideTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Blocks_and_overrides_are_recorded_and_reported()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        await scope.ServiceProvider.GetRequiredService<ReportCatalogueSeeder>().SeedAsync();
        (await db.Companies.SingleAsync(c => c.Id == companyId)).FundControlEnabled = true;
        (await db.Accounts.SingleAsync(a => a.CompanyId == companyId && a.AccountCode == "6800")).BudgetEnabled = true;
        await db.SaveChangesAsync();

        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        JournalRequest Spend(string document, string? reason = null, long? authoriser = null, long? preparer = null) => new()
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 5), TransactionCurrency = Scenario.Usd, DocumentType = "Journal", DocumentNumber = document, Narrative = "Stationery",
            BudgetOverrideReason = reason, BudgetOverrideAuthorisedByUserId = authoriser, SubmittedByUserId = preparer,
            Lines =
            [
                new JournalLineRequest { AccountCode = "6800", Side = JournalSide.Debit, TransactionAmount = 75m },
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 75m },
            ],
        };
        (await engine.PostAsync(Spend("FC-1"))).Error.Code.Should().Be("Posting.NoAppropriation");
        (await engine.PostAsync(Spend("FC-2", "Urgent repair", authoriser: 4, preparer: 4))).Error.Code.Should().Be("Posting.NoAppropriation"); // self-authorised
        (await engine.PostAsync(Spend("FC-3", "Urgent repair", authoriser: 9))).IsSuccess.Should().BeTrue();

        (await db.Set<BudgetControlEvent>().AsNoTracking().CountAsync(e => e.CompanyId == companyId)).Should().Be(3);

        var rendered = await scope.ServiceProvider.GetRequiredService<IReportRunner>().RenderAsync(companyId, "B27", new Dictionary<string, object?> { ["AsAtDate"] = new DateOnly(2026, 3, 31) }, ReportFormat.Csv);
        rendered.IsSuccess.Should().BeTrue();
        string report = Encoding.UTF8.GetString(rendered.Value.Content);
        report.Should().Contain(",Blocked,2026-03-05,6800,—,Journal FC-1,").And.Contain(",Blocked,2026-03-05,6800,—,Journal FC-2,")
            .And.Contain(",Overridden,2026-03-05,6800,—,Journal FC-3,").And.Contain(",Urgent repair,75"); // the authoriser shows by name when user 9 exists
    }
}
