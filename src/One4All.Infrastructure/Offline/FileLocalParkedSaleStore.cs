using System.Security.Cryptography;
using System.Text.Json;
using One4All.Application.Abstractions.Offline;

namespace One4All.Infrastructure.Offline;

/// <summary>Keeps the tabs parked offline as one sealed file on this computer, rewritten atomically on each change.</summary>
public sealed class FileLocalParkedSaleStore : ILocalParkedSaleStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    private readonly ILocalDataProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates the store.</summary>
    /// <param name="directory">The folder.</param>
    /// <param name="protector">Seals the file.</param>
    public FileLocalParkedSaleStore(string directory, ILocalDataProtector protector)
    {
        _path = Path.Combine(directory, "pos-parked.o4a");
        _protector = protector;
    }

    /// <summary>The store in the signed-in user's local application data.</summary>
    /// <param name="protector">Seals the file.</param>
    /// <returns>The store.</returns>
    public static FileLocalParkedSaleStore Default(ILocalDataProtector protector)
        => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "One4All"), protector);

    /// <inheritdoc />
    public async Task ParkAsync(LocalParkedSale sale, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sale);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            List<LocalParkedSale> all = [.. await ReadAsync(ct).ConfigureAwait(false), sale];
            await WriteAsync(all, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LocalParkedSale>> ListAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return [.. (await ReadAsync(ct).ConfigureAwait(false)).OrderBy(s => s.ParkedAt)];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<LocalParkedSale?> TakeAsync(Guid id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            List<LocalParkedSale> all = [.. await ReadAsync(ct).ConfigureAwait(false)];
            LocalParkedSale? taken = all.FirstOrDefault(s => s.Id == id);
            if (taken is not null)
            {
                all.Remove(taken);
                await WriteAsync(all, ct).ConfigureAwait(false);
            }

            return taken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<LocalParkedSale>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            byte[] sealedData = await File.ReadAllBytesAsync(_path, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<LocalParkedSale>>(_protector.Unprotect(sealedData), Json) ?? [];
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return []; // altered or sealed for another user: as if there were none
        }
    }

    private async Task WriteAsync(IReadOnlyList<LocalParkedSale> all, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + ".tmp";
        await File.WriteAllBytesAsync(temporary, _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(all, Json)), ct).ConfigureAwait(false);
        File.Move(temporary, _path, overwrite: true);
    }
}
