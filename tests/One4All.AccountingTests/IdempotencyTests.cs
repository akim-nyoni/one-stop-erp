using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Integration;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers idempotency keys: a journal posted twice with the same key posts once and returns the first journal; and the endpoint store
/// runs a keyed request once, replays its response to a retry, refuses the key for a different request, reports one still running,
/// lets the key go after a server failure, and purges keys past their retention.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class IdempotencyTests
{
    private readonly AccountingFixture _fixture;

    public IdempotencyTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_key_posts_once()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        IPostingEngine engine = scope.ServiceProvider.GetRequiredService<IPostingEngine>();
        string key = $"post-{Guid.NewGuid():N}";
        Task<Result<PostingReceipt>> Post(string? idempotencyKey, decimal amount) => engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = new DateOnly(2026, 4, 1), TransactionCurrency = Scenario.Usd, IdempotencyKey = idempotencyKey,
            Lines =
            [
                new JournalLineRequest { AccountCode = "1100", Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = "4000", Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        });

        PostingReceipt first = (await Post(key, 100m)).Value;
        PostingReceipt retry = (await Post(key, 100m)).Value;
        retry.Should().Be(first);
        (await db.Journals.AsNoTracking().CountAsync(j => j.CompanyId == companyId && j.IdempotencyKey == key)).Should().Be(1);
        (await Post($"other-{Guid.NewGuid():N}", 100m)).Value.JournalId.Should().NotBe(first.JournalId);
        (await Post(null, 100m)).Value.JournalId.Should().NotBe(first.JournalId);
    }

    [SkippableFact]
    public async Task The_store_replays_and_guards_keys()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IIdempotencyStore store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        long user = Random.Shared.NextInt64(1_000_000, 9_000_000);
        string key = $"k-{Guid.NewGuid():N}";

        IdempotencyClaim claim = await store.BeginAsync(user, key, "POST", "/api/trading/invoices", "HASH-A");
        claim.Outcome.Should().Be(IdempotencyOutcome.Proceed);
        (await store.BeginAsync(user, key, "POST", "/api/trading/invoices", "HASH-A")).Outcome.Should().Be(IdempotencyOutcome.InProgress);
        (await store.BeginAsync(user, key, "POST", "/api/trading/invoices", "HASH-B")).Outcome.Should().Be(IdempotencyOutcome.Mismatch);
        (await store.BeginAsync(user + 1, key, "POST", "/api/trading/invoices", "HASH-B")).Outcome.Should().Be(IdempotencyOutcome.Proceed, "keys belong to their user");

        await store.CompleteAsync(claim.ClaimId!.Value, 201, "application/json", "{\"id\":7}"u8.ToArray());
        IdempotencyClaim replay = await store.BeginAsync(user, key, "POST", "/api/trading/invoices", "HASH-A");
        (replay.Outcome, replay.Status, replay.ContentType, System.Text.Encoding.UTF8.GetString(replay.Body!)).Should().Be((IdempotencyOutcome.Replay, 201, "application/json", "{\"id\":7}"));

        // A server failure lets the key go.
        string failing = $"f-{Guid.NewGuid():N}";
        IdempotencyClaim failed = await store.BeginAsync(user, failing, "POST", "/api/x", "H");
        await store.AbandonAsync(failed.ClaimId!.Value);
        (await store.BeginAsync(user, failing, "POST", "/api/x", "H")).Outcome.Should().Be(IdempotencyOutcome.Proceed);

        // Keys past their retention are purged.
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        string old = $"o-{Guid.NewGuid():N}";
        db.IdempotencyRecords.Add(new IdempotencyRecord(user, old, "POST", "/api/x", "H", DateTimeOffset.UtcNow.AddYears(-1)));
        await db.SaveChangesAsync();
        (await store.PurgeAsync()).Should().BeGreaterThanOrEqualTo(1);
        (await db.IdempotencyRecords.AsNoTracking().AnyAsync(r => r.Key == old)).Should().BeFalse();
        (await db.IdempotencyRecords.AsNoTracking().AnyAsync(r => r.Key == key)).Should().BeTrue();
    }
}
