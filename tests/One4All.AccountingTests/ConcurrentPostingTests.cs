using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers two postings racing for the journal number sequence: while one user's posting holds its transaction open,
/// another user's posting reads the same next number, waits on the locked sequence row, loses the race when the first
/// commits — and is retried rather than failing, taking the following number. Both journals post, numbering stays
/// gapless and check data is clean.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ConcurrentPostingTests
{
    private readonly AccountingFixture _fixture;

    public ConcurrentPostingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_posting_that_loses_the_race_is_retried()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            companyId = await Scenario.SeedAsync(scope.ServiceProvider.GetRequiredService<One4AllDbContext>());
            (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(Journal(companyId, 10m))).Value.JournalNumber.Should().Be("JNL-2026-000001");
        }

        using SemaphoreSlim firstHoldsTheLock = new(0);
        Task<string> first = Task.Run(async () =>
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
            Result<PostingReceipt> posted = await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(Journal(companyId, 20m));
            firstHoldsTheLock.Release();
            await Task.Delay(1_500); // keep the sequence row locked while the second posting runs into it
            await transaction.CommitAsync();
            return posted.Value.JournalNumber;
        });

        await firstHoldsTheLock.WaitAsync();
        Task<Result<PostingReceipt>> second = Task.Run(async () =>
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(Journal(companyId, 30m));
        });

        string firstNumber = await first;
        Result<PostingReceipt> secondResult = await second;
        secondResult.IsSuccess.Should().BeTrue(secondResult.IsFailure ? secondResult.Error.ToString() : null);

        firstNumber.Should().Be("JNL-2026-000002");
        secondResult.Value.JournalNumber.Should().Be("JNL-2026-000003");

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            LedgerIntegrityReport check = (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value;
            check.Issues.Should().BeEmpty(string.Join("; ", check.Issues.Select(i => $"{i.Check} {i.Subject}: {i.Detail}")));
            ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
            tb.Rows.Single(r => r.AccountCode == "1000").Debit.Should().Be(60m);
        }
    }

    private static JournalRequest Journal(long companyId, decimal amount) => new()
    {
        CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 3, 10), TransactionCurrency = Scenario.Usd, EnforceManualJournalRules = false,
        Lines =
        [
            new JournalLineRequest { AccountCode = "1000", Side = JournalSide.Debit, TransactionAmount = amount },
            new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = amount },
        ],
    };
}
