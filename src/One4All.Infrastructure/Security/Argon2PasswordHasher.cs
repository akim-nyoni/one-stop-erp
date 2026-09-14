using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using One4All.Application.Abstractions.Security;

namespace One4All.Infrastructure.Security;

/// <summary>
/// Argon2id password/PIN hasher (brief §5.2). Produces a self-describing PHC-style string
/// (<c>$argon2id$v=19$m=..,t=..,p=..$salt$hash</c>) so the parameters and salt travel with the hash and
/// verification needs no external configuration. Verification is constant-time.
/// </summary>
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MemoryKib = 65536; // 64 MB
    private const int Iterations = 3;
    private const int Parallelism = 4;

    /// <inheritdoc />
    public string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Derive(password, salt, MemoryKib, Iterations, Parallelism, HashSize);
        return $"$argon2id$v=19$m={MemoryKib},t={Iterations},p={Parallelism}$" +
               $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <inheritdoc />
    public bool Verify(string hash, string password)
    {
        try
        {
            string[] parts = hash.Split('$', StringSplitOptions.RemoveEmptyEntries);
            // parts: [argon2id, v=19, m=..,t=..,p=.., salt, hash]
            if (parts.Length != 5 || parts[0] != "argon2id")
            {
                return false;
            }

            Dictionary<string, int> p = parts[2]
                .Split(',')
                .Select(kv => kv.Split('='))
                .ToDictionary(kv => kv[0], kv => int.Parse(kv[1]));

            byte[] salt = Convert.FromBase64String(parts[3]);
            byte[] expected = Convert.FromBase64String(parts[4]);
            byte[] actual = Derive(password, salt, p["m"], p["t"], p["p"], expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception ex) when (ex is FormatException or KeyNotFoundException or IndexOutOfRangeException)
        {
            return false;
        }
    }

    private static byte[] Derive(string password, byte[] salt, int memoryKib, int iterations, int parallelism, int size)
    {
        using Argon2id argon2 = new(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKib,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon2.GetBytes(size);
    }
}
