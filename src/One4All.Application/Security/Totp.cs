using System.Security.Cryptography;
using System.Text;

namespace One4All.Application.Security;

/// <summary>
/// Time-based one-time passwords (RFC 6238, HMAC-SHA1, 30-second steps, six digits) as authenticator apps use them (brief §5.2). Pure apart
/// from <see cref="NewSecret"/>.
/// </summary>
public static class Totp
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int StepSeconds = 30;

    /// <summary>A new random shared secret, Base32-encoded (160 bits).</summary>
    /// <returns>The secret.</returns>
    public static string NewSecret() => ToBase32(RandomNumberGenerator.GetBytes(20));

    /// <summary>The otpauth URI an authenticator app scans.</summary>
    /// <param name="issuer">The issuer (the product or company).</param>
    /// <param name="account">The account (user name).</param>
    /// <param name="secret">The Base32 secret.</param>
    /// <returns>The URI.</returns>
    public static string ProvisioningUri(string issuer, string account, string secret)
        => $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&digits=6&period={StepSeconds}";

    /// <summary>The code for a moment.</summary>
    /// <param name="secret">The Base32 secret.</param>
    /// <param name="at">The moment.</param>
    /// <returns>The six-digit code.</returns>
    public static string Code(string secret, DateTimeOffset at) => CodeForStep(FromBase32(secret), at.ToUnixTimeSeconds() / StepSeconds);

    /// <summary>Whether a code is valid at a moment, allowing one step either side for clock drift.</summary>
    /// <param name="secret">The Base32 secret.</param>
    /// <param name="code">The code given.</param>
    /// <param name="at">The moment.</param>
    /// <returns>True when valid.</returns>
    public static bool Verify(string secret, string? code, DateTimeOffset at)
    {
        string given = new([.. (code ?? string.Empty).Where(char.IsDigit)]);
        if (given.Length != 6)
        {
            return false;
        }

        byte[] key = FromBase32(secret);
        long step = at.ToUnixTimeSeconds() / StepSeconds;
        return Enumerable.Range(-1, 3).Any(d => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(CodeForStep(key, step + d)), Encoding.ASCII.GetBytes(given)));
    }

    private static string CodeForStep(byte[] key, long step)
    {
        byte[] counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counter);
        }

        byte[] hash = HMACSHA1.HashData(key, counter);
        int offset = hash[^1] & 0x0F;
        int binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ToBase32(byte[] data)
    {
        StringBuilder text = new();
        int buffer = 0, bits = 0;
        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                text.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            text.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return text.ToString();
    }

    private static byte[] FromBase32(string text)
    {
        List<byte> bytes = [];
        int buffer = 0, bits = 0;
        foreach (char c in text.TrimEnd('=').ToUpperInvariant().Where(c => c != ' '))
        {
            int value = Base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException("The secret is not Base32.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. bytes];
    }
}
