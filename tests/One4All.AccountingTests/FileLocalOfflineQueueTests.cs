using FluentAssertions;
using One4All.Application.Abstractions.Offline;
using One4All.Infrastructure.Offline;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the terminal-local file queue that captures sales while the central database is unreachable:
/// enqueue/list/remove round-trip, enqueue-once by operation id, capture-order listing, durability across
/// a new instance over the same directory, and tolerance of a corrupt entry file.
/// </summary>
public sealed class FileLocalOfflineQueueTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "one4all-offline-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private LocalOfflineEntry Entry(DateTimeOffset captured) =>
        new(Guid.NewGuid(), CompanyId: 1, "PosSale", "{\"SaleNumber\":\"X\"}", captured);

    [Fact]
    public async Task Enqueue_list_and_remove_round_trip()
    {
        FileLocalOfflineQueue queue = new(_dir);
        LocalOfflineEntry entry = Entry(DateTimeOffset.UtcNow);

        await queue.EnqueueAsync(entry);
        (await queue.CountAsync()).Should().Be(1);

        LocalOfflineEntry stored = (await queue.ListAsync()).Single();
        stored.OperationId.Should().Be(entry.OperationId);
        stored.PayloadJson.Should().Be(entry.PayloadJson);
        stored.CompanyId.Should().Be(1);

        await queue.RemoveAsync(entry.OperationId);
        (await queue.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Enqueue_is_idempotent_by_operation_id()
    {
        FileLocalOfflineQueue queue = new(_dir);
        LocalOfflineEntry entry = Entry(DateTimeOffset.UtcNow);

        await queue.EnqueueAsync(entry);
        await queue.EnqueueAsync(entry);

        (await queue.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Entries_list_in_capture_order()
    {
        FileLocalOfflineQueue queue = new(_dir);
        LocalOfflineEntry older = Entry(new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero));
        LocalOfflineEntry newer = Entry(new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.Zero));

        await queue.EnqueueAsync(newer);
        await queue.EnqueueAsync(older);

        (await queue.ListAsync()).Select(e => e.OperationId).Should().Equal(older.OperationId, newer.OperationId);
    }

    [Fact]
    public async Task Entries_survive_a_new_instance_over_the_same_directory()
    {
        LocalOfflineEntry entry = Entry(DateTimeOffset.UtcNow);
        await new FileLocalOfflineQueue(_dir).EnqueueAsync(entry);

        FileLocalOfflineQueue reopened = new(_dir);
        (await reopened.ListAsync()).Single().OperationId.Should().Be(entry.OperationId);
    }

    [Fact]
    public async Task A_corrupt_entry_file_is_ignored_not_thrown()
    {
        FileLocalOfflineQueue queue = new(_dir);
        await queue.EnqueueAsync(Entry(DateTimeOffset.UtcNow));
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Path.Combine(_dir, "garbage.json"), "{ not json ");

        (await queue.ListAsync()).Should().HaveCount(1); // the good one; the corrupt file is skipped
    }
}
