using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers posting again in a scope whose earlier posting was rolled back by the caller's transaction: the engine reads
/// the journal sequence and period balances afresh, so the next journal reuses the rolled-back number (no gap), the
/// period balances hold only committed movements, and nothing fails on a stale concurrency token.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class RollbackSafePostingTests
{
    private readonly AccountingFixture _fixture;

    public RollbackSafePostingTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_rolled_back_posting_leaves_no_trace_for_the_next_one()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            companyId = await Scenario.SeedAsync(scope.ServiceProvider.GetRequiredService<One4AllDbContext>());
            (await scope.ServiceProvider.GetRequiredService<IPostingEngine>().PostAsync(Journal(companyId, 100m))).Value.JournalNumber.Should().Be("JNL-2026-000001");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();

            await using (IDbContextTransaction transaction = await db.Database.BeginTransactionAsync())
            {
                (await engine.PostAsync(Journal(companyId, 50m))).Value.JournalNumber.Should().Be("JNL-2026-000002");
                await transaction.RollbackAsync();
            }

            Result<PostingReceipt> next = await engine.PostAsync(Journal(companyId, 30m));
            next.IsSuccess.Should().BeTrue(next.IsFailure ? next.Error.ToString() : null);
            next.Value.JournalNumber.Should().Be("JNL-2026-000002");
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            LedgerIntegrityReport check = (await scope.ServiceProvider.GetRequiredService<ILedgerIntegrityService>().CheckAsync(companyId)).Value;
            check.Issues.Should().BeEmpty(string.Join("; ", check.Issues.Select(i => $"{i.Check} {i.Subject}: {i.Detail}")));

            ConsolidatedTrialBalance tb = (await scope.ServiceProvider.GetRequiredService<IConsolidationService>().ConsolidatedTrialBalanceAsync([companyId], new DateOnly(2026, 3, 31))).Value;
            tb.Rows.Single(r => r.AccountCode == "1000").Debit.Should().Be(130m);
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
