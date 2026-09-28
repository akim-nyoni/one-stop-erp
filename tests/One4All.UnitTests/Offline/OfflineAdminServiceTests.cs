using FluentAssertions;
using One4All.Application.Abstractions.Offline;
using One4All.Application.Offline;
using One4All.Core.Common;
using One4All.Core.Offline;
using Xunit;

namespace One4All.UnitTests.Offline;

/// <summary>Covers the offline outbox domain transitions and the admin service (list/requeue/discard).</summary>
public sealed class OfflineAdminServiceTests
{
    private sealed class FakeOutbox : IOfflineOutbox
    {
        private readonly List<OfflineOperation> _ops = [];

        public int SaveCount { get; private set; }

        public void Seed(OfflineOperation op) => _ops.Add(op);

        public Task<bool> EnqueueAsync(OfflineOperation operation, CancellationToken ct = default)
        {
            _ops.Add(operation);
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<OfflineOperation>> GetPendingAsync(int maxCount, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OfflineOperation>>(_ops.Where(o => o.Status == OfflineOperationStatus.Pending).ToList());

        public Task<IReadOnlyList<OfflineOperation>> GetByStatusAsync(OfflineOperationStatus status, int maxCount, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OfflineOperation>>(_ops.Where(o => o.Status == status).Take(maxCount).ToList());

        public Task<OfflineOperation?> FindAsync(Guid operationId, CancellationToken ct = default) =>
            Task.FromResult(_ops.FirstOrDefault(o => o.OperationId == operationId));

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private static OfflineOperation Failed(int maxAttempts = 1)
    {
        OfflineOperation op = new(1, Guid.NewGuid(), "PosSale", "{}", DateTimeOffset.UtcNow);
        op.RecordFailure("boom", maxAttempts); // dead-letters at maxAttempts = 1
        return op;
    }

    // --- Domain transitions ---------------------------------------------------------------------------------

    [Fact]
    public void Requeue_resets_a_failed_operation_to_pending()
    {
        OfflineOperation op = Failed();
        op.Status.Should().Be(OfflineOperationStatus.Failed);

        op.Requeue().Should().BeTrue();
        op.Status.Should().Be(OfflineOperationStatus.Pending);
        op.Attempts.Should().Be(0);
        op.LastError.Should().BeNull();
    }

    [Fact]
    public void Discard_marks_a_failed_operation_discarded()
    {
        OfflineOperation op = Failed();
        op.Discard().Should().BeTrue();
        op.Status.Should().Be(OfflineOperationStatus.Discarded);
    }

    [Fact]
    public void Requeue_and_discard_reject_a_non_failed_operation()
    {
        OfflineOperation pending = new(1, Guid.NewGuid(), "PosSale", "{}", DateTimeOffset.UtcNow);
        pending.Requeue().Should().BeFalse();
        pending.Discard().Should().BeFalse();
        pending.Status.Should().Be(OfflineOperationStatus.Pending);
    }

    // --- Admin service --------------------------------------------------------------------------------------

    [Fact]
    public async Task ListFailed_returns_only_dead_lettered_operations()
    {
        FakeOutbox outbox = new();
        outbox.Seed(Failed());
        outbox.Seed(new OfflineOperation(1, Guid.NewGuid(), "PosSale", "{}", DateTimeOffset.UtcNow)); // pending
        OfflineAdminService admin = new(outbox);

        (await admin.ListFailedAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Requeue_returns_the_operation_to_pending_and_saves()
    {
        FakeOutbox outbox = new();
        OfflineOperation op = Failed();
        outbox.Seed(op);
        OfflineAdminService admin = new(outbox);

        Result result = await admin.RequeueAsync(op.OperationId);

        result.IsSuccess.Should().BeTrue();
        op.Status.Should().Be(OfflineOperationStatus.Pending);
        outbox.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Requeue_of_a_missing_operation_is_not_found()
    {
        OfflineAdminService admin = new(new FakeOutbox());
        Result result = await admin.RequeueAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Offline.NotFound");
    }

    [Fact]
    public async Task Requeue_of_a_non_failed_operation_is_a_rule_violation()
    {
        FakeOutbox outbox = new();
        OfflineOperation pending = new(1, Guid.NewGuid(), "PosSale", "{}", DateTimeOffset.UtcNow);
        outbox.Seed(pending);
        OfflineAdminService admin = new(outbox);

        Result result = await admin.RequeueAsync(pending.OperationId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Offline.NotFailed");
    }

    [Fact]
    public async Task Discard_marks_the_operation_discarded_and_saves()
    {
        FakeOutbox outbox = new();
        OfflineOperation op = Failed();
        outbox.Seed(op);
        OfflineAdminService admin = new(outbox);

        (await admin.DiscardAsync(op.OperationId)).IsSuccess.Should().BeTrue();
        op.Status.Should().Be(OfflineOperationStatus.Discarded);
    }
}
