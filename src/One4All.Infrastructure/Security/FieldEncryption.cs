using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace One4All.Infrastructure.Security;

/// <summary>
/// Keys for column-level encryption (brief §4.15.10: payroll salary, bank details and national ID encrypted at rest with keys held
/// outside the database). Each key has an id so keys can be rotated: values carry the id they were written with, new writes use the
/// active key, and the maintenance task re-encrypts old rows.
/// </summary>
public sealed class FieldEncryptionOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "FieldEncryption";

    /// <summary>Gets or sets the keys (id → base64 256-bit key).</summary>
    public Dictionary<string, string> Keys { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the key new values are written with.</summary>
    public string? ActiveKeyId { get; set; }

    /// <summary>
    /// The keys to use: those configured, else <c>ONE4ALL_FIELD_KEY</c> (base64, id "env"), else a development key file in the user's local
    /// application data (created once). A production server configures its own keys.
    /// </summary>
    /// <param name="options">Configured options (may be null).</param>
    /// <returns>The options in force, and whether a development key is being used.</returns>
    public static (FieldEncryptionOptions Options, bool Development) Resolve(FieldEncryptionOptions? options)
    {
        if (options is { Keys.Count: > 0 })
        {
            options.ActiveKeyId ??= options.Keys.Keys.Last();
            return (options, false);
        }

        if (Environment.GetEnvironmentVariable("ONE4ALL_FIELD_KEY") is string fromEnvironment && fromEnvironment.Length > 0)
        {
            return (new FieldEncryptionOptions { Keys = { ["env"] = fromEnvironment }, ActiveKeyId = "env" }, false);
        }

        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "One4All");
        string file = Path.Combine(folder, "field-encryption-dev.key");
        if (!File.Exists(file))
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(file, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        }

        return (new FieldEncryptionOptions { Keys = { ["dev"] = File.ReadAllText(file).Trim() }, ActiveKeyId = "dev" }, true);
    }
}

/// <summary>
/// AES-256-GCM encryption of single values: <c>enc:v1:{keyId}:{base64(nonce | ciphertext | tag)}</c>. A value without the prefix is
/// plaintext written before encryption was turned on and is returned as it is, so existing rows keep working until re-encrypted.
/// </summary>
public sealed class FieldCipher
{
    private const string Prefix = "enc:v1:";
    private readonly Dictionary<string, byte[]> _keys;
    private readonly string _activeKeyId;

    /// <summary>Creates the cipher.</summary>
    /// <param name="options">The keys.</param>
    /// <exception cref="ArgumentException">Thrown when a key is not 32 bytes or the active key is missing.</exception>
    public FieldCipher(FieldEncryptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _keys = options.Keys.ToDictionary(k => k.Key, k => Convert.FromBase64String(k.Value), StringComparer.Ordinal);
        if (_keys.Values.Any(k => k.Length != 32))
        {
            throw new ArgumentException("Field encryption keys must be 256 bits (32 bytes, base64).");
        }

        _activeKeyId = options.ActiveKeyId is string active && _keys.ContainsKey(active) ? active : throw new ArgumentException("The active field encryption key is not among the keys.");
    }

    /// <summary>Gets the key new values are written with.</summary>
    public string ActiveKeyId => _activeKeyId;

    /// <summary>Whether a stored value is encrypted (and with which key).</summary>
    /// <param name="stored">The stored value.</param>
    /// <returns>The key id, or null for plaintext.</returns>
    public static string? KeyIdOf(string? stored)
        => stored is not null && stored.StartsWith(Prefix, StringComparison.Ordinal) && stored.IndexOf(':', Prefix.Length) is int colon and > 0 ? stored[Prefix.Length..colon] : null;

    /// <summary>Encrypts a value (null stays null).</summary>
    /// <param name="plaintext">The value.</param>
    /// <returns>The stored form.</returns>
    public string? Protect(string? plaintext)
    {
        if (plaintext is null)
        {
            return null;
        }

        byte[] data = Encoding.UTF8.GetBytes(plaintext);
        byte[] nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        byte[] cipher = new byte[data.Length];
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using (AesGcm aes = new(_keys[_activeKeyId], tag.Length))
        {
            aes.Encrypt(nonce, data, cipher, tag, Encoding.UTF8.GetBytes(_activeKeyId));
        }

        return $"{Prefix}{_activeKeyId}:{Convert.ToBase64String([.. nonce, .. cipher, .. tag])}";
    }

    /// <summary>Decrypts a stored value (plaintext from before encryption is returned unchanged).</summary>
    /// <param name="stored">The stored value.</param>
    /// <returns>The value.</returns>
    /// <exception cref="CryptographicException">Thrown when the key is unknown or the value was tampered with.</exception>
    public string? Unprotect(string? stored)
    {
        if (KeyIdOf(stored) is not string keyId)
        {
            return stored;
        }

        if (!_keys.TryGetValue(keyId, out byte[]? key))
        {
            throw new CryptographicException($"The field encryption key '{keyId}' is not configured.");
        }

        byte[] blob = Convert.FromBase64String(stored![(Prefix.Length + keyId.Length + 1)..]);
        int nonceSize = AesGcm.NonceByteSizes.MaxSize, tagSize = AesGcm.TagByteSizes.MaxSize;
        byte[] plain = new byte[blob.Length - nonceSize - tagSize];
        using AesGcm aes = new(key, tagSize);
        aes.Decrypt(blob.AsSpan(0, nonceSize), blob.AsSpan(nonceSize, plain.Length), blob.AsSpan(nonceSize + plain.Length), plain, Encoding.UTF8.GetBytes(keyId));
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>Encrypts a decimal (invariant text).</summary>
    /// <param name="value">The value.</param>
    /// <returns>The stored form.</returns>
    public string ProtectDecimal(decimal value) => Protect(value.ToString(CultureInfo.InvariantCulture))!;

    /// <summary>Decrypts a decimal.</summary>
    /// <param name="stored">The stored form.</param>
    /// <returns>The value.</returns>
    public decimal UnprotectDecimal(string stored) => decimal.Parse(Unprotect(stored) ?? "0", NumberStyles.Number, CultureInfo.InvariantCulture);
}

/// <summary>
/// The process-wide cipher the EF value converters use (the model is built once and cached, so converters cannot take a per-context
/// dependency); set when the infrastructure is registered.
/// </summary>
public static class FieldCipherHolder
{
    private static FieldCipher? _cipher;

    /// <summary>Gets or sets the cipher.</summary>
    public static FieldCipher Cipher
    {
        get => _cipher ?? throw new InvalidOperationException("Field encryption is not configured.");
        set => _cipher = value;
    }

    /// <summary>Encrypts (for converters).</summary>
    /// <param name="value">The value.</param>
    /// <returns>The stored form.</returns>
    public static string? Protect(string? value) => Cipher.Protect(value);

    /// <summary>Decrypts (for converters).</summary>
    /// <param name="value">The stored form.</param>
    /// <returns>The value.</returns>
    public static string? Unprotect(string? value) => Cipher.Unprotect(value);

    /// <summary>Encrypts a decimal (for converters).</summary>
    /// <param name="value">The value.</param>
    /// <returns>The stored form.</returns>
    public static string ProtectDecimal(decimal value) => Cipher.ProtectDecimal(value);

    /// <summary>Decrypts a decimal (for converters).</summary>
    /// <param name="value">The stored form.</param>
    /// <returns>The value.</returns>
    public static decimal UnprotectDecimal(string value) => Cipher.UnprotectDecimal(value);
}

/// <summary>Maps properties to encrypted text columns.</summary>
public static class FieldEncryptionModel
{
    /// <summary>The annotation marking an encrypted property (the audit log records "[encrypted]" for it).</summary>
    public const string Annotation = "One4All:Encrypted";

    private static readonly ValueConverter<string, string> Text = new(v => FieldCipherHolder.Protect(v)!, v => FieldCipherHolder.Unprotect(v)!);
    private static readonly ValueConverter<decimal, string> Number = new(v => FieldCipherHolder.ProtectDecimal(v), v => FieldCipherHolder.UnprotectDecimal(v));

    /// <summary>Stores a string or decimal property encrypted in a text column (plaintext written before is still read; nulls are not converted).</summary>
    /// <param name="property">The property.</param>
    /// <returns>The builder.</returns>
    public static PropertyBuilder Encrypted(this PropertyBuilder property)
    {
        ArgumentNullException.ThrowIfNull(property);
        Type type = property.Metadata.ClrType;
        ValueConverter converter = type == typeof(string) ? Text : type == typeof(decimal) ? Number
            : throw new NotSupportedException($"{property.Metadata.Name}: only text and decimal properties can be encrypted.");
        return property.HasConversion(converter).HasColumnType("text").HasAnnotation(Annotation, true);
    }
}
