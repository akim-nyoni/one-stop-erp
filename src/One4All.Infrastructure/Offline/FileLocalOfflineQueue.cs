using System.Security.Cryptography;
using System.Text.Json;
using One4All.Application.Abstractions.Offline;

namespace One4All.Infrastructure.Offline;

/// <summary>
/// A terminal-local offline queue backed by one file per captured entry under a directory on the terminal (default
/// <c>%LOCALAPPDATA%/One4All/offline-queue</c>). It does not touch the database, so it keeps working when the central server is
/// unreachable; entries survive a process restart and are handed off to the central <see cref="IOfflineOutbox"/> on reconnect. Files are
/// named by operation id, which makes enqueue idempotent and remove exact. With a protector each entry is sealed (<c>.o4a</c>); plain
/// <c>.json</c> entries written before encryption are still read, flushed and removed.
/// </summary>
public sealed class FileLocalOfflineQueue : ILocalOfflineQueue
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _directory;
    private readonly ILocalDataProtector? _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates the queue over a directory (created on first use).</summary>
    /// <param name="directory">The directory that holds the entry files.</param>
    /// <param name="protector">Seals each entry (null: plain JSON).</param>
    public FileLocalOfflineQueue(string directory, ILocalDataProtector? protector = null)
    {
        _directory = directory;
        _protector = protector;
    }

    /// <summary>Creates the queue over the default per-user terminal directory.</summary>
    /// <param name="protector">Seals each entry.</param>
    /// <returns>The queue.</returns>
    public static FileLocalOfflineQueue Default(ILocalDataProtector? protector = null) => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "One4All", "offline-queue"), protector);

    /// <inheritdoc />
    public async Task EnqueueAsync(LocalOfflineEntry entry, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            if (File.Exists(SealedPath(entry.OperationId)) || File.Exists(PlainPath(entry.OperationId)))
            {
                return; // enqueue-once by operation id
            }

            byte[] json = JsonSerializer.SerializeToUtf8Bytes(entry, Json);
            string path = _protector is null ? PlainPath(entry.OperationId) : SealedPath(entry.OperationId);
            string temporary = path + ".tmp";
            await File.WriteAllBytesAsync(temporary, _protector is null ? json : _protector.Protect(json), ct).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: false);
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
            foreach (string path in EntryFiles())
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
        foreach (string path in new[] { SealedPath(operationId), PlainPath(operationId) })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken ct = default) => Task.FromResult(Directory.Exists(_directory) ? EntryFiles().Count() : 0);

    private IEnumerable<string> EntryFiles() => Directory.EnumerateFiles(_directory, "*.json").Concat(Directory.EnumerateFiles(_directory, "*.o4a"));

    private string PlainPath(Guid operationId) => Path.Combine(_directory, $"{operationId:N}.json");

    private string SealedPath(Guid operationId) => Path.Combine(_directory, $"{operationId:N}.o4a");

    private async Task<LocalOfflineEntry?> ReadAsync(string path, CancellationToken ct)
    {
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            if (path.EndsWith(".o4a", StringComparison.OrdinalIgnoreCase))
            {
                if (_protector is null)
                {
                    return null;
                }

                bytes = _protector.Unprotect(bytes);
            }

            return JsonSerializer.Deserialize<LocalOfflineEntry>(bytes, Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or CryptographicException)
        {
            return null; // ignore a corrupt/locked/foreign entry rather than blocking the whole flush
        }
    }
}
