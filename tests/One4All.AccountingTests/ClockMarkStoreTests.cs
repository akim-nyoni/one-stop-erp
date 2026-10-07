using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Infrastructure.Licensing;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the clock-rollback high-water mark: it is kept sealed in both the database and a file and only moves forward; deleting the file or
/// emptying the database does not wind it back; a tampered or foreign copy is ignored. (The validator's refusal of a clock set back beyond
/// the mark is covered by the licensing unit tests.)
/// </summary>
[Collection(AccountingCollection.Name)]
public sealed class ClockMarkStoreTests
{
    private readonly AccountingFixture _fixture;

    public ClockMarkStoreTests(AccountingFixture fixture) => _fixture = fixture;

    private sealed class Machine(params string[] parts) : IMachineIdProvider
    {
        public IReadOnlyList<string> GetComponents() => parts;
    }

    [SkippableFact]
    public async Task The_mark_survives_losing_either_copy_and_never_moves_back()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        IServiceScopeFactory scopes = provider.GetRequiredService<IServiceScopeFactory>();
        async Task ClearDatabase()
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<One4AllDbContext>().Set<ClockMark>().ExecuteDeleteAsync();
        }

        await ClearDatabase();
        string file = Path.Combine(Path.GetTempPath(), $"one4all-clock-{Guid.NewGuid():N}.mark");
        try
        {
            ClockHighWaterMarkStore store = new(scopes, new Machine("CPU-1", "BOARD-1"), file);
            store.Read().Should().BeNull();
            DateTimeOffset march = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
            store.Advance(march);
            store.Advance(march.AddDays(-10));
            File.ReadAllText(file).Should().NotContain("2026", "the file copy is sealed");

            // A fresh process (no cache) reads it back from either copy alone.
            File.Delete(file);
            new ClockHighWaterMarkStore(scopes, new Machine("CPU-1", "BOARD-1"), file).Read().Should().Be(march);
            ClockHighWaterMarkStore again = new(scopes, new Machine("CPU-1", "BOARD-1"), file);
            again.Advance(march.AddHours(2));
            await ClearDatabase();
            new ClockHighWaterMarkStore(scopes, new Machine("CPU-1", "BOARD-1"), file).Read().Should().Be(march.AddHours(2));

            // Tampered with, or sealed on another machine: not trusted.
            File.WriteAllText(file, "garbage");
            new ClockHighWaterMarkStore(scopes, new Machine("CPU-1", "BOARD-1"), file).Read().Should().BeNull();
            ClockHighWaterMarkStore other = new(scopes, new Machine("CPU-9", "BOARD-9"), file);
            File.WriteAllText(file, other.Seal(march));
            new ClockHighWaterMarkStore(scopes, new Machine("CPU-1", "BOARD-1"), file).Read().Should().BeNull();

            // Advancing again from a real copy keeps working after a bad copy was found.
            ClockHighWaterMarkStore guarded = new(scopes, new Machine("CPU-1", "BOARD-1"), file);
            guarded.Advance(march);
            new ClockHighWaterMarkStore(scopes, new Machine("CPU-1", "BOARD-1"), file).Read().Should().Be(march);
        }
        finally
        {
            File.Delete(file);
            await ClearDatabase();
        }
    }
}
