using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the DbContext's company query filter (brief §2): a context scoped to one company cannot read another company's rows — by
/// code, by id, or when deleted rows are included — while an unscoped context sees every company, and the intercompany and consolidation
/// services, which work across companies by design, still do so from a scoped request.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class CompanyScopeFilterTests
{
    private readonly AccountingFixture _fixture;

    public CompanyScopeFilterTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_scoped_context_reads_only_its_company_until_the_scope_is_lifted()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long mine;
        long theirs;
        long theirAccount;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            mine = await Scenario.SeedAsync(db);
            theirs = await Scenario.SeedAsync(db);
            theirAccount = await db.Accounts.Where(a => a.CompanyId == theirs && a.AccountCode == "1100").Select(a => a.Id).SingleAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Accounts.CountAsync(a => a.Id == theirAccount)).Should().Be(1, "an unscoped context sees every company");

            db.CompanyScope = mine;
            (await db.Accounts.Where(a => a.CompanyId == mine || a.CompanyId == theirs).Select(a => a.CompanyId).Distinct().ToListAsync()).Should().Equal(mine);
            (await db.Accounts.FindAsync(theirAccount)).Should().BeNull("another company's row is invisible even by its id");
            (await db.Accounts.IgnoreQueryFilters([One4AllDbContext.SoftDeleteFilter]).AnyAsync(a => a.Id == theirAccount)).Should().BeFalse(
                "including deleted rows still keeps to the company");

            using (db.WithoutCompanyScope())
            {
                (await db.Accounts.AnyAsync(a => a.Id == theirAccount)).Should().BeTrue();
            }

            db.CompanyScope.Should().Be(mine, "the scope comes back when the handle is disposed");
            (await db.Accounts.AnyAsync(a => a.Id == theirAccount)).Should().BeFalse();
        }
    }

    [SkippableFact]
    public async Task Intercompany_and_consolidation_work_across_companies_from_a_scoped_request()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long parent;
        long subsidiary;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            parent = await Scenario.SeedAsync(db);
            subsidiary = await Scenario.SeedAsync(db);
            db.Accounts.Add(new LedgerAccount(parent, "1700", "Due from Subsidiary", AccountType.Asset));
            db.Accounts.Add(new LedgerAccount(subsidiary, "2700", "Due to Parent", AccountType.Liability));
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            db.CompanyScope = parent;

            var posted = await scope.ServiceProvider.GetRequiredService<IIntercompanyService>().PostAsync(new IntercompanyJournalRequest
            {
                FromCompanyId = parent, ToCompanyId = subsidiary, Amount = 400m, Date = new DateOnly(2026, 3, 15), DocumentNumber = "IC-SCOPE",
                FromAccountCode = "1100", FromIntercompanyAccountCode = "1700", ToAccountCode = "1100", ToIntercompanyAccountCode = "2700",
            });
            posted.IsSuccess.Should().BeTrue(posted.IsFailure ? posted.Error.ToString() : null);
            db.CompanyScope.Should().Be(parent);

            ConsolidatedTrialBalance group = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>()
                .ConsolidatedTrialBalanceAsync([parent, subsidiary], new DateOnly(2026, 3, 31), ["1700", "2700"])).Value;
            group.IntercompanyEliminated.Should().Be(400m);
            group.IsBalanced.Should().BeTrue();

            (await db.Journals.AnyAsync(j => j.CompanyId == subsidiary && j.DocumentNumber == "IC-SCOPE")).Should().BeFalse("the subsidiary's half is outside the parent's scope");
        }
    }
}
