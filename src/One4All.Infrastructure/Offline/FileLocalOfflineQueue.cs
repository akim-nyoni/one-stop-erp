using System.Text.Json;
using One4All.Application.Abstractions.Offline;

namespace One4All.Infrastructure.Offline;

/// <summary>
/// A terminal-local offline queue backed by one JSON file per captured entry under a directory on the
/// terminal (default <c>%LOCALAPPDATA%/One4All/offline-queue</c>). It does not touch the database, so it
/// keeps working when the central server is unreachable; entries survive a process restart and are handed
/// off to the central <see cref="IOfflineOutbox"/> on reconnect. Files are named by operation id, which
/// makes enqueue idempotent and remove exact.
/// </summary>
public sealed class FileLocalOfflineQueue : ILocalOfflineQueue
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates the queue over a directory (created on first use).</summary>
    /// <param name="directory">The directory that holds the entry files.</param>
    public FileLocalOfflineQueue(string directory) => _directory = directory;

    /// <summary>Creates the queue over the default per-user terminal directory.</summary>
    /// <returns>The queue.</returns>
    public static FileLocalOfflineQueue Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "One4All", "offline-queue"));

    /// <inheritdoc />
    public async Task EnqueueAsync(LocalOfflineEntry entry, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            string path = PathFor(entry.OperationId);
            if (File.Exists(path))
            {
                return; // enqueue-once by operation id
            }

            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(entry, Json), ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LocalOfflineEntry>> ListAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_directory))
            {
                return [];
            }

            List<LocalOfflineEntry> entries = [];
            foreach (string path in Directory.EnumerateFiles(_directory, "*.json"))
            {
                LocalOfflineEntry? entry = await ReadAsync(path, ct).ConfigureAwait(false);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }

            return entries.OrderBy(e => e.CapturedUtc).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task RemoveAsync(Guid operationId, CancellationToken ct = default)
    {
        string path = PathFor(operationId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken ct = default)
    {
        int count = Directory.Exists(_directory) ? Directory.EnumerateFiles(_directory, "*.json").Count() : 0;
        return Task.FromResult(count);
    }

    private string PathFor(Guid operationId) => Path.Combine(_directory, $"{operationId:N}.json");

    private static async Task<LocalOfflineEntry?> ReadAsync(string path, CancellationToken ct)
    {
        try
        {
            string text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<LocalOfflineEntry>(text, Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null; // ignore a corrupt/locked entry rather than blocking the whole flush
        }
    }
}
