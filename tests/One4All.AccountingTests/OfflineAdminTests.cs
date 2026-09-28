using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Offline;
using One4All.Core.Offline;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// End-to-end offline outbox administration over the real database: an operation with no handler is
/// dead-lettered, listed as failed, requeued (and fails again), then discarded.
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class OfflineAdminTests
{
    private readonly AccountingFixture _fixture;

    public OfflineAdminTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task A_dead_lettered_operation_can_be_listed_requeued_and_discarded()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();

        long companyId;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            companyId = await Scenario.SeedAsync(db);
        }

        Guid operationId = Guid.NewGuid();
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IOfflineOutbox outbox = scope.ServiceProvider.GetRequiredService<IOfflineOutbox>();
            IClock clock = scope.ServiceProvider.GetRequiredService<IClock>();
            await outbox.EnqueueAsync(new OfflineOperation(companyId, operationId, "MysteryKind", "{}", clock.UtcNow));
        }

        // Drain: the unknown kind is dead-lettered.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            OfflineSyncReport report = await scope.ServiceProvider.GetRequiredService<IOfflineSyncService>().SyncPendingAsync();
            report.DeadLettered.Should().Be(1);
        }

        // It appears in the failed list.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IOfflineAdminService admin = scope.ServiceProvider.GetRequiredService<IOfflineAdminService>();
            (await admin.ListFailedAsync()).Should().Contain(o => o.OperationId == operationId);

            // Requeue returns it to pending.
            (await admin.RequeueAsync(operationId)).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            OfflineOperation op = await db.OfflineOperations.SingleAsync(o => o.OperationId == operationId);
            op.Status.Should().Be(OfflineOperationStatus.Pending);
            op.Attempts.Should().Be(0);
        }

        // Draining again dead-letters it once more (still no handler), then it is discarded.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOfflineSyncService>().SyncPendingAsync();
            (await scope.ServiceProvider.GetRequiredService<IOfflineAdminService>().DiscardAsync(operationId)).IsSuccess.Should().BeTrue();
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
            OfflineOperation op = await db.OfflineOperations.SingleAsync(o => o.OperationId == operationId);
            op.Status.Should().Be(OfflineOperationStatus.Discarded);

            // A discarded operation is never picked up by the drain again.
            (await scope.ServiceProvider.GetRequiredService<IOfflineSyncService>().SyncPendingAsync()).Processed.Should().Be(0);
        }
    }
}
