using System.Security.Cryptography;
using One4All.Application.Abstractions.Offline;

namespace One4All.Infrastructure.Offline;

/// <summary>
/// Seals the terminal's local data with AES-GCM under a random 256-bit key kept beside it; on Windows the key file itself is protected
/// with DPAPI for the signed-in user, so a copied disk or another account cannot open the store. Layout: version (1), nonce (12), tag (16),
/// ciphertext.
/// </summary>
public sealed class LocalDataProtector : ILocalDataProtector
{
    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Entropy = "One4All.LocalStore"u8.ToArray();
    private readonly Lazy<byte[]> _key;

    /// <summary>Creates the protector.</summary>
    /// <param name="directory">The folder holding the key file.</param>
    public LocalDataProtector(string directory) => _key = new Lazy<byte[]>(() => LoadOrCreateKey(Path.Combine(directory, "local.key")));

    /// <summary>The protector for the signed-in user's local store.</summary>
    /// <returns>The protector.</returns>
    public static LocalDataProtector Default() => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "One4All"));

    /// <inheritdoc />
    public byte[] Protect(byte[] plain)
    {
        ArgumentNullException.ThrowIfNull(plain);
        byte[] output = new byte[1 + NonceSize + TagSize + plain.Length];
        output[0] = Version;
        Span<byte> nonce = output.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using AesGcm aes = new(_key.Value, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(1 + NonceSize + TagSize), output.AsSpan(1 + NonceSize, TagSize));
        return output;
    }

    /// <inheritdoc />
    public byte[] Unprotect(byte[] sealedData)
    {
        ArgumentNullException.ThrowIfNull(sealedData);
        if (sealedData.Length < 1 + NonceSize + TagSize || sealedData[0] != Version)
        {
            throw new CryptographicException("Not a sealed local store entry.");
        }

        byte[] plain = new byte[sealedData.Length - 1 - NonceSize - TagSize];
        using AesGcm aes = new(_key.Value, TagSize);
        aes.Decrypt(sealedData.AsSpan(1, NonceSize), sealedData.AsSpan(1 + NonceSize + TagSize), sealedData.AsSpan(1 + NonceSize, TagSize), plain);
        return plain;
    }

    private static byte[] LoadOrCreateKey(string path)
    {
        if (File.Exists(path))
        {
            byte[] stored = File.ReadAllBytes(path);
            return OperatingSystem.IsWindows() ? ProtectedData.Unprotect(stored, Entropy, DataProtectionScope.CurrentUser) : stored;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] toStore = OperatingSystem.IsWindows() ? ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser) : key;
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, toStore);
        File.Move(temporary, path, overwrite: false);
        return key;
    }
}
