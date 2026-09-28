using FluentAssertions;
using One4All.Application.Abstractions.Offline;
using One4All.Application.Offline;
using One4All.Core.Common;
using One4All.Core.Offline;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.UnitTests.Offline;

/// <summary>
/// Covers the offline sync engine with in-memory fakes: successful replay, immediate dead-lettering when
/// no handler is registered, retry-then-dead-letter on repeated failure, isolation of a throwing handler,
/// capture-order processing, and the outbox's enqueue-once idempotency.
/// </summary>
public sealed class OfflineSyncServiceTests
{
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeOutbox : IOfflineOutbox
    {
        private readonly List<OfflineOperation> _ops = [];

        public int SaveCount { get; private set; }

        public IReadOnlyList<OfflineOperation> All => _ops;

        public Task<bool> EnqueueAsync(OfflineOperation operation, CancellationToken ct = default)
        {
            if (_ops.Any(o => o.OperationId == operation.OperationId))
            {
                return Task.FromResult(false);
            }

            _ops.Add(operation);
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<OfflineOperation>> GetPendingAsync(int maxCount, CancellationToken ct = default)
        {
            IReadOnlyList<OfflineOperation> pending = _ops
                .Where(o => o.Status == OfflineOperationStatus.Pending)
                .OrderBy(o => o.CreatedUtc)
                .Take(maxCount)
                .ToList();
            return Task.FromResult(pending);
        }

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHandler(string kind, Func<OfflineOperation, Result> replay) : IOfflineOperationHandler
    {
        public string Kind { get; } = kind;

        public List<Guid> Seen { get; } = [];

        public Task<Result> ReplayAsync(OfflineOperation operation, CancellationToken ct = default)
        {
            Seen.Add(operation.OperationId);
            return Task.FromResult(replay(operation));
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private static OfflineOperation Op(string kind, DateTimeOffset created) =>
        new(companyId: 1, Guid.NewGuid(), kind, "{}", created);

    private static OfflineSyncService Engine(FakeOutbox outbox, OfflineSyncOptions? options, params IOfflineOperationHandler[] handlers) =>
        new(outbox, handlers, new FixedClock(Now), options);

    [Fact]
    public async Task Successful_replay_marks_the_operation_synced()
    {
        FakeOutbox outbox = new();
        OfflineOperation op = Op("PosSale", Now);
        await outbox.EnqueueAsync(op);
        FakeHandler handler = new("PosSale", _ => Result.Success());

        OfflineSyncReport report = await Engine(outbox, null, handler).SyncPendingAsync();

        report.Synced.Should().Be(1);
        report.Processed.Should().Be(1);
        op.Status.Should().Be(OfflineOperationStatus.Synced);
        op.SyncedUtc.Should().Be(Now);
        outbox.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task An_operation_with_no_handler_is_dead_lettered_immediately()
    {
        FakeOutbox outbox = new();
        OfflineOperation op = Op("Unknown", Now);
        await outbox.EnqueueAsync(op);

        OfflineSyncReport report = await Engine(outbox, null).SyncPendingAsync();

        report.DeadLettered.Should().Be(1);
        op.Status.Should().Be(OfflineOperationStatus.Failed);
        op.LastError.Should().Contain("Unknown");
    }

    [Fact]
    public async Task A_failing_operation_is_retried_then_dead_lettered_at_the_attempt_ceiling()
    {
        FakeOutbox outbox = new();
        OfflineOperation op = Op("PosSale", Now);
        await outbox.EnqueueAsync(op);
        FakeHandler handler = new("PosSale", _ => Result.Failure(Error.Rule("X", "nope")));
        OfflineSyncService engine = Engine(outbox, new OfflineSyncOptions { MaxAttempts = 3 }, handler);

        (await engine.SyncPendingAsync()).Retryable.Should().Be(1);
        op.Status.Should().Be(OfflineOperationStatus.Pending);
        op.Attempts.Should().Be(1);

        await engine.SyncPendingAsync();
        op.Attempts.Should().Be(2);
        op.Status.Should().Be(OfflineOperationStatus.Pending);

        OfflineSyncReport third = await engine.SyncPendingAsync();
        third.DeadLettered.Should().Be(1);
        op.Attempts.Should().Be(3);
        op.Status.Should().Be(OfflineOperationStatus.Failed);

        // Once dead-lettered it is no longer pending, so a further run does nothing.
        (await engine.SyncPendingAsync()).Processed.Should().Be(0);
    }

    [Fact]
    public async Task A_throwing_handler_is_treated_as_a_retryable_failure_not_a_crash()
    {
        FakeOutbox outbox = new();
        OfflineOperation op = Op("PosSale", Now);
        await outbox.EnqueueAsync(op);
        FakeHandler handler = new("PosSale", _ => throw new InvalidOperationException("boom"));

        OfflineSyncReport report = await Engine(outbox, new OfflineSyncOptions { MaxAttempts = 5 }, handler).SyncPendingAsync();

        report.Retryable.Should().Be(1);
        op.Status.Should().Be(OfflineOperationStatus.Pending);
        op.LastError.Should().Contain("boom");
    }

    [Fact]
    public async Task Operations_are_replayed_in_capture_order()
    {
        FakeOutbox outbox = new();
        OfflineOperation second = Op("PosSale", Now.AddMinutes(2));
        OfflineOperation first = Op("PosSale", Now);
        await outbox.EnqueueAsync(second); // enqueued out of order
        await outbox.EnqueueAsync(first);
        FakeHandler handler = new("PosSale", _ => Result.Success());

        await Engine(outbox, null, handler).SyncPendingAsync();

        handler.Seen.Should().Equal(first.OperationId, second.OperationId);
    }

    [Fact]
    public async Task Enqueueing_the_same_operation_id_twice_is_ignored()
    {
        FakeOutbox outbox = new();
        Guid id = Guid.NewGuid();
        OfflineOperation a = new(1, id, "PosSale", "{}", Now);
        OfflineOperation b = new(1, id, "PosSale", "{}", Now);

        (await outbox.EnqueueAsync(a)).Should().BeTrue();
        (await outbox.EnqueueAsync(b)).Should().BeFalse();
        outbox.All.Should().ContainSingle();
    }
}
