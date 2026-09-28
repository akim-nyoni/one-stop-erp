using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Accounting;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers multi-company consolidation: two companies' ledgers sum into one balanced trial balance, and
/// companies with different base currencies are refused.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ConsolidationTests
{
    private readonly AccountingFixture _fixture;

    public ConsolidationTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Two_companies_consolidate_into_one_balanced_trial_balance()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyA;
        long companyB;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyA = await Scenario.SeedAsync(db);
            companyB = await Scenario.SeedAsync(db);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            await Post(engine, companyA, 500m);
            await Post(engine, companyB, 300m);
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IConsolidationService consolidation = scope.ServiceProvider.GetRequiredService<IConsolidationService>();
            var result = await consolidation.ConsolidatedTrialBalanceAsync([companyA, companyB], new DateOnly(2026, 3, 31));

            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            ConsolidatedTrialBalance tb = result.Value;

            tb.IsBalanced.Should().BeTrue();
            tb.TotalDebit.Should().Be(800m);   // 500 + 300 cash
            tb.TotalCredit.Should().Be(800m);  // 500 + 300 sales
            tb.Rows.Should().Contain(r => r.AccountCode == "1000" && r.Debit == 800m);
            tb.Rows.Should().Contain(r => r.AccountCode == "4000" && r.Credit == 800m);

            // Consolidating one company alone shows only its own figure.
            var single = await consolidation.ConsolidatedTrialBalanceAsync([companyA], new DateOnly(2026, 3, 31));
            single.Value.Rows.Should().Contain(r => r.AccountCode == "1000" && r.Debit == 500m);
        }
    }

    [SkippableFact]
    public async Task Companies_with_different_base_currencies_are_refused()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long usdCompany;
        long eurCompany;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            usdCompany = await Scenario.SeedAsync(db);
            Company eur = new("EURCO", "Euro Co", Scenario.Eur, "UTC");
            db.Companies.Add(eur);
            await db.SaveChangesAsync();
            eurCompany = eur.Id;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IConsolidationService consolidation = scope.ServiceProvider.GetRequiredService<IConsolidationService>();
            var result = await consolidation.ConsolidatedTrialBalanceAsync([usdCompany, eurCompany], new DateOnly(2026, 3, 31));
            result.Error.Code.Should().Be("Consolidation.MixedCurrency");
        }
    }

    private static async Task Post(IPostingEngine engine, long companyId, decimal amount)
    {
        var result = await engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd,
            EnforceManualJournalRules = false, DocumentType = "Journal", DocumentNumber = "CON",
            Lines =
            [
                new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        });
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
    }
}
