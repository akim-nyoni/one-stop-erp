using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.Licensing;

/// <summary>The latest time the licence check has seen, kept encrypted in the database (<c>lic.clock_mark</c>).</summary>
public sealed class ClockMark
{
    private ClockMark() => Sealed = string.Empty;

    /// <summary>Creates the mark.</summary>
    /// <param name="sealed">The encrypted value.</param>
    public ClockMark(string @sealed) => Sealed = @sealed;

    /// <summary>The id (one row).</summary>
    public int Id { get; private set; }

    /// <summary>The encrypted time.</summary>
    public string Sealed { get; set; }
}

/// <summary>
/// The clock-rollback high-water mark (brief §11.5): the latest time seen, sealed with AES-GCM under a key bound to this machine, kept both
/// in the database and in a file. The mark read is the later of the two, so deleting or restoring either one does not wind it back; an
/// unreadable copy (tampered with, or from another machine) is ignored. It only ever moves forward, and is written at most once a minute.
/// </summary>
public sealed class ClockHighWaterMarkStore : ILicenseHighWaterMarkStore
{
    private static readonly TimeSpan WriteEvery = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly string _filePath;
    private readonly byte[] _key;
    private readonly object _gate = new();
    private DateTimeOffset? _cached;

    /// <summary>Creates the store.</summary>
    /// <param name="scopes">Opens database scopes.</param>
    /// <param name="machineId">The machine identity the key is bound to.</param>
    /// <param name="filePath">The file copy (default: the common application data folder).</param>
    public ClockHighWaterMarkStore(IServiceScopeFactory scopes, IMachineIdProvider machineId, string? filePath = null)
    {
        _scopes = scopes;
        _filePath = filePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "One4All", "clock.mark");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes("One4All.ClockMark|" + string.Join('|', machineId.GetComponents())));
    }

    /// <inheritdoc />
    public DateTimeOffset? Read()
    {
        lock (_gate)
        {
            DateTimeOffset? file = Unseal(ReadFile()), database = Unseal(ReadDatabase());
            _cached = Max(Max(file, database), _cached);
            return _cached;
        }
    }

    /// <inheritdoc />
    public void Advance(DateTimeOffset value)
    {
        lock (_gate)
        {
            if (_cached is DateTimeOffset known && value < known + WriteEvery)
            {
                return;
            }

            DateTimeOffset next = Max(value, _cached) ?? value;
            string sealedValue = Seal(next);
            WriteFile(sealedValue);
            WriteDatabase(sealedValue);
            _cached = next;
        }
    }

    /// <summary>Seals a time (exposed for tests).</summary>
    /// <param name="value">The time.</param>
    /// <returns>The sealed text.</returns>
    public string Seal(DateTimeOffset value)
    {
        byte[] plain = Encoding.UTF8.GetBytes(value.UtcTicks.ToString(CultureInfo.InvariantCulture));
        byte[] nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using AesGcm aes = new(_key, tag.Length);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    /// <summary>Opens a sealed time, or null when it cannot be trusted.</summary>
    /// <param name="sealedValue">The sealed text.</param>
    /// <returns>The time, or null.</returns>
    public DateTimeOffset? Unseal(string? sealedValue)
    {
        if (string.IsNullOrWhiteSpace(sealedValue))
        {
            return null;
        }

        try
        {
            byte[] all = Convert.FromBase64String(sealedValue);
            int nonceSize = AesGcm.NonceByteSizes.MaxSize, tagSize = AesGcm.TagByteSizes.MaxSize;
            byte[] plain = new byte[all.Length - nonceSize - tagSize];
            using AesGcm aes = new(_key, tagSize);
            aes.Decrypt(all.AsSpan(0, nonceSize), all.AsSpan(nonceSize + tagSize), all.AsSpan(nonceSize, tagSize), plain);
            return new DateTimeOffset(long.Parse(Encoding.UTF8.GetString(plain), CultureInfo.InvariantCulture), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b) => a is null ? b : b is null ? a : (a > b ? a : b);

    private string? ReadFile()
    {
        try
        {
            return File.Exists(_filePath) ? File.ReadAllText(_filePath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void WriteFile(string sealedValue)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, sealedValue);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The database copy still holds the mark.
        }
    }

    private string? ReadDatabase()
    {
        using IServiceScope scope = _scopes.CreateScope();
        return scope.ServiceProvider.GetRequiredService<One4AllDbContext>().Set<ClockMark>().AsNoTracking().OrderBy(m => m.Id).Select(m => m.Sealed).FirstOrDefault();
    }

    private void WriteDatabase(string sealedValue)
    {
        using IServiceScope scope = _scopes.CreateScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        ClockMark? mark = db.Set<ClockMark>().OrderBy(m => m.Id).FirstOrDefault();
        if (mark is null)
        {
            db.Set<ClockMark>().Add(new ClockMark(sealedValue));
        }
        else
        {
            mark.Sealed = sealedValue;
        }

        db.SaveChanges();
    }
}
