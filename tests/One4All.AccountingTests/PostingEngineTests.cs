using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

[Collection(AccountingCollection.Name)]
public sealed class PostingEngineTests
{
    private readonly AccountingFixture _fixture;

    public PostingEngineTests(AccountingFixture fixture) => _fixture = fixture;

    private static JournalLineRequest Line(string account, JournalSide side, decimal amount, decimal? rate = null) =>
        new() { AccountCode = account, Side = side, TransactionAmount = amount, ExchangeRate = rate };

    [SkippableFact]
    public async Task Balanced_journal_posts_and_period_balances_reconcile_to_zero()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            companyId = await Scenario.SeedAsync(scope.ServiceProvider.GetRequiredService<One4AllDbContext>());
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            JournalRequest request = new()
            {
                CompanyId = companyId,
                SourceModule = "GL",
                PostingDate = new DateOnly(2026, 3, 15),
                TransactionCurrency = Scenario.Usd,
                EnforceManualJournalRules = true,
                Narrative = "Cash sale",
                Lines = [Line("1000", JournalSide.Debit, 115m), Line("4000", JournalSide.Credit, 115m)],
            };

            Result<PostingReceipt> result = await engine.PostAsync(request);
            result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
            result.Value.JournalNumber.Should().StartWith("JNL-2026-");
        }

        // The fundamental invariant: all period-balance debits equal all credits.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            List<GlPeriodBalance> balances = await db.PeriodBalances.Where(b => b.CompanyId == companyId).ToListAsync();
            balances.Sum(b => b.BaseDebit).Should().Be(balances.Sum(b => b.BaseCredit));
            balances.Sum(b => b.NetBase).Should().Be(0m); // trial balance nets to zero
        }
    }

    [SkippableFact]
    public async Task Unbalanced_journal_is_rejected()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            companyId = await Scenario.SeedAsync(scope.ServiceProvider.GetRequiredService<One4AllDbContext>());
        }

        await using AsyncServiceScope scope2 = provider.CreateAsyncScope();
        IPostingEngine engine = scope2.ServiceProvider.GetRequiredService<IPostingEngine>();
        JournalRequest request = new()
        {
            CompanyId = companyId,
            PostingDate = new DateOnly(2026, 3, 15),
            TransactionCurrency = Scenario.Usd,
            Lines = [Line("1000", JournalSide.Debit, 100m), Line("4000", JournalSide.Credit, 90m)],
        };

        Result<PostingReceipt> result = await engine.PostAsync(request);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Posting.OutOfBalance");
    }

    [SkippableFact]
    public async Task Manual_journal_to_control_account_is_rejected()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            companyId = await Scenario.SeedAsync(scope.ServiceProvider.GetRequiredService<One4AllDbContext>());
        }

        await using AsyncServiceScope scope2 = provider.CreateAsyncScope();
        IPostingEngine engine = scope2.ServiceProvider.GetRequiredService<IPostingEngine>();
        JournalRequest request = new()
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = new DateOnly(2026, 3, 15),
            TransactionCurrency = Scenario.Usd,
            EnforceManualJournalRules = true,
            Lines = [Line("1200", JournalSide.Debit, 50m), Line("4000", JournalSide.Credit, 50m)],
        };

        Result<PostingReceipt> result = await engine.PostAsync(request);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Posting.ControlAccountManualJournal");
    }

    [SkippableFact]
    public async Task Subledger_may_post_to_control_account()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            companyId = await Scenario.SeedAsync(scope.ServiceProvider.GetRequiredService<One4AllDbContext>());
        }

        await using AsyncServiceScope scope2 = provider.CreateAsyncScope();
        IPostingEngine engine = scope2.ServiceProvider.GetRequiredService<IPostingEngine>();
        JournalRequest request = new()
        {
            CompanyId = companyId,
            SourceModule = "AR",
            PostingDate = new DateOnly(2026, 3, 15),
            TransactionCurrency = Scenario.Usd,
            EnforceManualJournalRules = false, // sub-ledger posting
            DocumentType = "SalesInvoice",
            Lines =
            [
                Line("1200", JournalSide.Debit, 115m),  // AR control
                Line("4000", JournalSide.Credit, 100m), // sales
                Line("2200", JournalSide.Credit, 15m),  // VAT output
            ],
        };

        Result<PostingReceipt> result = await engine.PostAsync(request);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
    }

    [SkippableFact]
    public async Task Posting_to_a_closed_period_is_rejected()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            // Do not open periods.
            companyId = await Scenario.SeedAsync(scope.ServiceProvider.GetRequiredService<One4AllDbContext>(), openPeriods: false);
        }

        await using AsyncServiceScope scope2 = provider.CreateAsyncScope();
        IPostingEngine engine = scope2.ServiceProvider.GetRequiredService<IPostingEngine>();
        JournalRequest request = new()
        {
            CompanyId = companyId,
            PostingDate = new DateOnly(2026, 3, 15),
            TransactionCurrency = Scenario.Usd,
            Lines = [Line("1000", JournalSide.Debit, 10m), Line("4000", JournalSide.Credit, 10m)],
        };

        Result<PostingReceipt> result = await engine.PostAsync(request);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Posting.PeriodNotOpen");
    }

    [SkippableFact]
    public async Task Multi_currency_journal_converts_to_base_and_balances()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            companyId = await Scenario.SeedAsync(scope.ServiceProvider.GetRequiredService<One4AllDbContext>());
        }

        await using AsyncServiceScope scope2 = provider.CreateAsyncScope();
        IPostingEngine engine = scope2.ServiceProvider.GetRequiredService<IPostingEngine>();
        // EUR invoice: 100 EUR to bank, 100 EUR to sales; rate looked up (1.10) -> 110 USD base each side.
        JournalRequest request = new()
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = new DateOnly(2026, 6, 1),
            TransactionCurrency = Scenario.Eur,
            EnforceManualJournalRules = true,
            Lines = [Line("1100", JournalSide.Debit, 100m), Line("4000", JournalSide.Credit, 100m)],
        };

        Result<PostingReceipt> result = await engine.PostAsync(request);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : null);
        result.Value.TotalBase.Should().Be(110m);
    }

    [SkippableFact]
    public async Task Journal_numbers_are_sequential()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            companyId = await Scenario.SeedAsync(scope.ServiceProvider.GetRequiredService<One4AllDbContext>());
        }

        string first;
        string second;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            first = (await engine.PostAsync(Simple(companyId, 10m))).Value.JournalNumber;
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
            second = (await engine.PostAsync(Simple(companyId, 20m))).Value.JournalNumber;
        }

        first.Should().Be("JNL-2026-000001");
        second.Should().Be("JNL-2026-000002");
    }

    private static JournalRequest Simple(long companyId, decimal amount) => new()
    {
        CompanyId = companyId,
        SourceModule = "GL",
        PostingDate = new DateOnly(2026, 2, 1),
        TransactionCurrency = Scenario.Usd,
        EnforceManualJournalRules = true,
        Lines = [Line("1000", JournalSide.Debit, amount), Line("4000", JournalSide.Credit, amount)],
    };
}
