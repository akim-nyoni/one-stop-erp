using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace One4All.Licensing.Core;

/// <summary>
/// Encrypts the vendor's Ed25519 private key at rest with a passphrase (brief §11.2): PBKDF2-SHA256 to
/// derive a key, then AES-256-GCM for authenticated encryption. The on-disk form is a small JSON blob
/// carrying the salt, nonce, ciphertext, tag and iteration count - everything needed to decrypt except
/// the passphrase.
/// </summary>
public sealed class ProtectedPrivateKey
{
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int DefaultIterations = 300_000;

    /// <summary>PBKDF2 salt (Base64).</summary>
    [JsonPropertyName("salt")]
    public string Salt { get; set; } = string.Empty;

    /// <summary>AES-GCM nonce (Base64).</summary>
    [JsonPropertyName("nonce")]
    public string Nonce { get; set; } = string.Empty;

    /// <summary>Ciphertext (Base64).</summary>
    [JsonPropertyName("ciphertext")]
    public string Ciphertext { get; set; } = string.Empty;

    /// <summary>GCM authentication tag (Base64).</summary>
    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;

    /// <summary>PBKDF2 iteration count.</summary>
    [JsonPropertyName("iterations")]
    public int Iterations { get; set; } = DefaultIterations;

    /// <summary>Encrypts a private key with a passphrase.</summary>
    /// <param name="privateKeyBase64">The Base64 private key to protect.</param>
    /// <param name="passphrase">The passphrase.</param>
    /// <returns>The protected key blob.</returns>
    public static ProtectedPrivateKey Protect(string privateKeyBase64, string passphrase)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), salt, DefaultIterations, HashAlgorithmName.SHA256, KeySize);

        byte[] plaintext = Encoding.UTF8.GetBytes(privateKeyBase64);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using (AesGcm aes = new(key, TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        return new ProtectedPrivateKey
        {
            Salt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
            Ciphertext = Convert.ToBase64String(ciphertext),
            Tag = Convert.ToBase64String(tag),
            Iterations = DefaultIterations,
        };
    }

    /// <summary>Decrypts the private key with the passphrase.</summary>
    /// <param name="passphrase">The passphrase.</param>
    /// <returns>The Base64 private key.</returns>
    /// <exception cref="CryptographicException">Thrown when the passphrase is wrong or the blob was tampered with.</exception>
    public string Unprotect(string passphrase)
    {
        byte[] salt = Convert.FromBase64String(Salt);
        byte[] nonce = Convert.FromBase64String(Nonce);
        byte[] ciphertext = Convert.FromBase64String(Ciphertext);
        byte[] tag = Convert.FromBase64String(Tag);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        byte[] plaintext = new byte[ciphertext.Length];
        using (AesGcm aes = new(key, TagSize))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        return Encoding.UTF8.GetString(plaintext);
    }

    /// <summary>Serializes the blob to JSON.</summary>
    /// <returns>JSON text.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Parses a blob from JSON.</summary>
    /// <param name="json">JSON text.</param>
    /// <returns>The protected key, or null if invalid.</returns>
    public static ProtectedPrivateKey? FromJson(string json) => JsonSerializer.Deserialize<ProtectedPrivateKey>(json);
}
