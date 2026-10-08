using System.Security.Cryptography;
using System.Text.Json;
using One4All.Application.Abstractions.Offline;

namespace One4All.Infrastructure.Offline;

/// <summary>Keeps the terminal's offline snapshot as one sealed file, replaced atomically on each save.</summary>
public sealed class FileLocalCatalogueStore : ILocalCatalogueStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    private readonly ILocalDataProtector _protector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates the store.</summary>
    /// <param name="directory">The folder.</param>
    /// <param name="protector">Seals the file.</param>
    public FileLocalCatalogueStore(string directory, ILocalDataProtector protector)
    {
        _path = Path.Combine(directory, "pos-catalogue.o4a");
        _protector = protector;
    }

    /// <summary>The store in the signed-in user's local application data.</summary>
    /// <param name="protector">Seals the file.</param>
    /// <returns>The store.</returns>
    public static FileLocalCatalogueStore Default(ILocalDataProtector protector)
        => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "One4All"), protector);

    /// <inheritdoc />
    public async Task SaveAsync(PosOfflineSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        byte[] sealedData = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(snapshot, Json));
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temporary = _path + ".tmp";
            await File.WriteAllBytesAsync(temporary, sealedData, ct).ConfigureAwait(false);
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<PosOfflineSnapshot?> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            byte[] sealedData = await File.ReadAllBytesAsync(_path, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<PosOfflineSnapshot>(_protector.Unprotect(sealedData), Json);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            return null; // altered, sealed for another user, or unreadable: as if there were none
        }
        finally
        {
            _gate.Release();
        }
    }
}
