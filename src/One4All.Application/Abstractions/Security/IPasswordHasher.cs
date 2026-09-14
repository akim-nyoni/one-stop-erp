namespace One4All.Application.Abstractions.Security;

/// <summary>
/// Hashes and verifies passwords and PINs. The implementation uses Argon2id (brief §5.2); the salt and
/// parameters are encoded in the returned hash string so verification is self-contained.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a secret (password or PIN).</summary>
    /// <param name="password">The plaintext secret.</param>
    /// <returns>An encoded hash safe to persist.</returns>
    string Hash(string password);

    /// <summary>Verifies a secret against a stored hash.</summary>
    /// <param name="hash">The stored hash.</param>
    /// <param name="password">The plaintext secret to check.</param>
    /// <returns>True when the secret matches.</returns>
    bool Verify(string hash, string password);
}
