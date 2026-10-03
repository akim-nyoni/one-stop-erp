using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Schools;
using One4All.Core.Accounting;
using One4All.Core.Schools;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers school fees billed in advance and earned over the term (brief §3.7, IFRS 15): billing credits deferred income
/// rather than revenue; the end-of-January run earns 31 of the term's 90 days; a repeat run posts nothing; the
/// end-of-term run earns the rest.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class DeferredSchoolFeesTests
{
    private readonly AccountingFixture _fixture;

    public DeferredSchoolFeesTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Term_fees_are_deferred_and_earned_over_the_term()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        long structureId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
            db.Accounts.Add(new LedgerAccount(companyId, "2490", "Fees Received in Advance", AccountType.Liability));

            Customer guardianA = new(companyId, "DSF-A", "Guardian A", Scenario.Usd);
            Customer guardianB = new(companyId, "DSF-B", "Guardian B", Scenario.Usd);
            db.Customers.AddRange(guardianA, guardianB);

            FeeStructure structure = new(companyId, "DSF-G1T1", "Grade 1 Term 1", "G1", "Term 1", "4200");
            structure.AddItem("Tuition", 1_000m, true);
            structure.DeferOverTerm(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), "2490");
            db.FeeStructures.Add(structure);
            await db.SaveChangesAsync();

            Student full = new(companyId, "DSF-001", "Full Fee", "G1", guardianA.Id);
            Student bursary = new(companyId, "DSF-002", "Bursary", "G1", guardianB.Id);
            bursary.GrantBursary(BursaryType.Percentage, 25m);
            db.Students.AddRange(full, bursary);
            await db.SaveChangesAsync();
            structureId = structure.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            var run = await scope.ServiceProvider.GetRequiredService<ISchoolBillingService>().BillTermAsync(companyId, structureId, "DSF-T1-", new DateOnly(2026, 1, 1));
            run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.ToString() : null);
            run.Value.TotalNetBilled.Should().Be(1_750m);

            // Billed to deferred income, not revenue.
            ConsolidatedTrialBalance billed = await Tb(scope, companyId, new DateOnly(2026, 1, 1));
            billed.Rows.Single(r => r.AccountCode == "2490").Credit.Should().Be(1_750m);
            billed.Rows.Should().NotContain(r => r.AccountCode == "4200");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IDeferredIncomeService deferred = scope.ServiceProvider.GetRequiredService<IDeferredIncomeService>();

            // January: 31 of 90 days — 344.44 + 258.33.
            DeferredIncomeRunResult january = (await deferred.RecogniseAsync(companyId, new DateOnly(2026, 1, 31))).Value;
            january.Recognised.Should().Be(602.77m);
            january.JournalNumber.Should().NotBeNull();
            (await deferred.RecogniseAsync(companyId, new DateOnly(2026, 1, 31))).Value.JournalNumber.Should().BeNull();

            ConsolidatedTrialBalance afterJanuary = await Tb(scope, companyId, new DateOnly(2026, 1, 31));
            afterJanuary.Rows.Single(r => r.AccountCode == "4200").Credit.Should().Be(602.77m);
            afterJanuary.Rows.Single(r => r.AccountCode == "2490").Credit.Should().Be(1_147.23m);

            // End of term: the rest.
            DeferredIncomeRunResult march = (await deferred.RecogniseAsync(companyId, new DateOnly(2026, 3, 31))).Value;
            march.Recognised.Should().Be(1_147.23m);

            ConsolidatedTrialBalance endOfTerm = await Tb(scope, companyId, new DateOnly(2026, 3, 31));
            endOfTerm.IsBalanced.Should().BeTrue();
            endOfTerm.Rows.Single(r => r.AccountCode == "4200").Credit.Should().Be(1_750m);
            endOfTerm.Rows.Should().NotContain(r => r.AccountCode == "2490");
        }
    }

    private static async Task<ConsolidatedTrialBalance> Tb(AsyncServiceScope scope, long companyId, DateOnly asAt)
        => (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], asAt)).Value;
}
