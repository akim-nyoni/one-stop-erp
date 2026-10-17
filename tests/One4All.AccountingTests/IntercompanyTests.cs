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
/// Covers intercompany postings: the mirrored pair lands in both companies (each balancing), the due-from and
/// due-to agree, a failure on the second side rolls back the first, and consolidation eliminates the pair.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class IntercompanyTests
{
    private const string DueFrom = "1700";
    private const string DueTo = "2700";

    private readonly AccountingFixture _fixture;

    public IntercompanyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Intercompany_pair_posts_atomically_and_eliminates_on_consolidation()
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
            db.Accounts.Add(new LedgerAccount(parent, DueFrom, "Due from Subsidiary", AccountType.Asset));
            db.Accounts.Add(new LedgerAccount(subsidiary, DueTo, "Due to Parent", AccountType.Liability));
            // Note: only the parent has 1700 and only the subsidiary has 2700 - the mirror is deliberate.
            await db.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IIntercompanyService interco = scope.ServiceProvider.GetRequiredService<IIntercompanyService>();

            // Validation.
            (await interco.PostAsync(Request(parent, parent, 100m))).Error.Code.Should().Be("Interco.SameCompany");
            (await interco.PostAsync(Request(parent, subsidiary, 0m))).Error.Code.Should().Be("Interco.Amount");
            (await interco.PostAsync(Request(parent, 999_999, 100m))).Error.Code.Should().Be("Interco.CompanyNotFound");

            // Parent funds the subsidiary's bank with 1,000: parent Dr 1700 / Cr 1100, subsidiary Dr 1100 / Cr 2700.
            var posted = await interco.PostAsync(Request(parent, subsidiary, 1000m));
            posted.IsSuccess.Should().BeTrue(posted.IsFailure ? posted.Error.ToString() : null);
            posted.Value.FromJournalNumber.Should().NotBeNullOrEmpty();
            posted.Value.ToJournalNumber.Should().NotBeNullOrEmpty();

            // Second side fails (the subsidiary has no account 9999) → nothing from the first side survives.
            var failed = await interco.PostAsync(Request(parent, subsidiary, 50m) with { ToAccountCode = "9999", DocumentNumber = "IC-BAD" });
            failed.IsFailure.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            (await db.Journals.AnyAsync(j => j.DocumentNumber == "IC-BAD" && (j.CompanyId == parent || j.CompanyId == subsidiary))).Should().BeFalse();

            IConsolidationService consolidation = scope.ServiceProvider.GetRequiredService<IConsolidationService>();

            // Each company's own trial balance balances and shows its half.
            ConsolidatedTrialBalance parentTb = (await consolidation.ConsolidatedTrialBalanceAsync([parent], new DateOnly(2026, 3, 31))).Value;
            parentTb.IsBalanced.Should().BeTrue();
            parentTb.Rows.Should().Contain(r => r.AccountCode == DueFrom && r.Debit == 1000m);
            parentTb.Rows.Should().Contain(r => r.AccountCode == "1100" && r.Credit == 1000m);

            ConsolidatedTrialBalance subTb = (await consolidation.ConsolidatedTrialBalanceAsync([subsidiary], new DateOnly(2026, 3, 31))).Value;
            subTb.IsBalanced.Should().BeTrue();
            subTb.Rows.Should().Contain(r => r.AccountCode == DueTo && r.Credit == 1000m);
            subTb.Rows.Should().Contain(r => r.AccountCode == "1100" && r.Debit == 1000m);

            // Without elimination the group shows both intercompany balances; with it they vanish and the bank nets to zero.
            ConsolidatedTrialBalance gross = (await consolidation.ConsolidatedTrialBalanceAsync([parent, subsidiary], new DateOnly(2026, 3, 31))).Value;
            gross.Rows.Should().Contain(r => r.AccountCode == DueFrom).And.Contain(r => r.AccountCode == DueTo);
            gross.IntercompanyEliminated.Should().Be(0m);

            ConsolidatedTrialBalance net = (await consolidation.ConsolidatedTrialBalanceAsync([parent, subsidiary], new DateOnly(2026, 3, 31), [DueFrom, DueTo])).Value;
            net.IsBalanced.Should().BeTrue();
            net.IntercompanyEliminated.Should().Be(1000m);
            net.IntercompanyUnmatched.Should().Be(0m);
            net.Rows.Should().NotContain(r => r.AccountCode == DueFrom).And.NotContain(r => r.AccountCode == DueTo);
            net.Rows.Should().NotContain(r => r.AccountCode == "1100"); // −1000 + 1000 = 0, so no row

            // An unmatched residual is reported and left visible rather than silently dropped.
            ConsolidatedTrialBalance partial = (await consolidation.ConsolidatedTrialBalanceAsync([parent, subsidiary], new DateOnly(2026, 3, 31), [DueFrom])).Value;
            partial.IntercompanyEliminated.Should().Be(0m);
            partial.IntercompanyUnmatched.Should().Be(1000m);
            partial.Rows.Should().Contain(r => r.AccountCode == DueFrom);

            // Sides that do not net exactly are eliminated to the extent they match; the residue stays visible on the account (thirteenth audit #9).
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            (await engine.PostAsync(new JournalRequest
            {
                CompanyId = parent, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 20), TransactionCurrency = Scenario.Usd, DocumentNumber = "IC-ONE-SIDED",
                Lines = [new JournalLineRequest { AccountCode = DueFrom, Side = JournalSide.Debit, TransactionAmount = 400m }, new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Credit, TransactionAmount = 400m }],
            })).IsSuccess.Should().BeTrue();
            ConsolidatedTrialBalance mostly = (await consolidation.ConsolidatedTrialBalanceAsync([parent, subsidiary], new DateOnly(2026, 3, 31), [DueFrom, DueTo])).Value;
            (mostly.IntercompanyEliminated, mostly.IntercompanyUnmatched).Should().Be((1000m, 400m));
            mostly.Rows.Should().Contain(r => r.AccountCode == DueFrom && r.Debit == 400m).And.NotContain(r => r.AccountCode == DueTo);
            mostly.IsBalanced.Should().BeTrue();
        }
    }

    private static IntercompanyJournalRequest Request(long from, long to, decimal amount) => new()
    {
        FromCompanyId = from, ToCompanyId = to, Amount = amount, Date = new DateOnly(2026, 3, 15), DocumentNumber = "IC-1",
        FromAccountCode = "1100", FromIntercompanyAccountCode = DueFrom, ToAccountCode = "1100", ToIntercompanyAccountCode = DueTo,
    };
}
